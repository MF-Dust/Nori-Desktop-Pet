using System.Runtime.CompilerServices;
using System.Text.Json;
using Nori.Plugin.Abstractions;
using Nori.Plugin.Harness.Abstractions;

namespace Nori.Plugin.Runtime;

/// <summary>插件生命周期状态。</summary>
public enum PluginLifecycleState
{
	Discovered,
	Installed,
	Loading,
	Active,
	Stopping,
	Disabled,
	Failed,
	Incompatible,
	PendingRestart,
}

/// <summary>插件宿主运行时配置。</summary>
public sealed record PluginRuntimeOptions
{
	public required string PluginsDirectory { get; init; }
	public required string DataDirectory { get; init; }
	public PluginApiVersion HostApiVersion { get; init; } = new(1, 0);
	public PluginVersion HostVersion { get; init; } = new(1, 0, 0);
	public bool DevelopmentHost { get; init; }
	public bool SafeMode { get; init; }
	public IReadOnlyCollection<string> KnownCapabilityIds { get; init; } =
	[
		PluginCapabilityIds.WebView,
		PluginCapabilityIds.Arcade,
	];
	public Func<PluginDescriptor, CancellationToken, IEnumerable<IPluginCapability>>? CapabilityFactory { get; init; }
	public Func<string, string, Uri>? AssetUriFactory { get; init; }
	public Action<PluginException>? OnError { get; init; }
	public Action<PluginDescriptor, string, Exception?>? OnLog { get; init; }
	public TimeSpan ActivationTimeout { get; init; } = TimeSpan.FromSeconds(15);
	public TimeSpan DeactivationTimeout { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>插件当前状态快照。</summary>
public sealed record PluginInfo(
	string Id,
	PluginManifest Manifest,
	PluginLifecycleState State,
	string? ErrorCode);

/// <summary>一个带全局 ID 的 Harness 工具。</summary>
public sealed record PluginHarnessTool(string Id, IHarnessTool Tool);

/// <summary>插件发现、加载、激活、停用和卸载管理器。</summary>
public sealed class PluginManager : IAsyncDisposable
{
	private readonly PluginRuntimeOptions _options;
	private readonly PluginPackageInstaller _installer;
	private readonly PluginLoader _loader = new();
	private readonly Dictionary<string, PluginHandle> _plugins = new(StringComparer.Ordinal);
	private readonly CancellationTokenSource _shutdownSource = new();
	private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
	private readonly string _startupStatePath;
	private readonly Dictionary<string, StartupState> _startupStates;
	private int _disposed;

	public PluginManager(PluginRuntimeOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentException.ThrowIfNullOrWhiteSpace(options.PluginsDirectory);
		ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectory);
		if (options.ActivationTimeout <= TimeSpan.Zero || options.DeactivationTimeout <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(options), "插件生命周期超时必须大于零");
		_options = options;
		Directory.CreateDirectory(options.PluginsDirectory);
		Directory.CreateDirectory(options.DataDirectory);
		string runtimeDirectory = Path.Combine(options.DataDirectory, "runtime");
		Directory.CreateDirectory(runtimeDirectory);
		_startupStatePath = Path.Combine(runtimeDirectory, "plugin-startup.json");
		_startupStates = LoadStartupStates(_startupStatePath);
		_installer = new PluginPackageInstaller(options.PluginsDirectory);
	}

	public IReadOnlyCollection<PluginInfo> Plugins => _plugins.Values.Select(handle => handle.Info).ToArray();
	public PluginPackageInstaller Installer => _installer;

	/// <summary>读取当前版本的 manifest。发现阶段不会创建插件 ALC。</summary>
	public IReadOnlyCollection<PluginInfo> Discover()
	{
		EnsureNotDisposed();
		if (!_options.SafeMode) InstallInboxPackages();
		foreach (string id in _installer.InstalledIds.Order(StringComparer.Ordinal))
		{
			try
			{
				string? directory = _installer.ResolveCurrentDirectory(id);
				if (directory is null) throw new PluginException(PluginErrorCodes.InvalidPackage, "current.json 指向的版本不存在");
				PluginManifest manifest = PluginManifestReader.Read(Path.Combine(directory, PluginPackageInstaller.ManifestFileName));
				if (!string.Equals(id, manifest.Id, StringComparison.Ordinal))
					throw new PluginException(PluginErrorCodes.InvalidManifest, "插件目录 ID 与 manifest.json 不一致");
				PluginLifecycleState state = _options.SafeMode ? PluginLifecycleState.Disabled : PluginLifecycleState.Discovered;
				string? errorCode = _options.SafeMode ? PluginErrorCodes.SafeModeDisabled : null;
				if (!_options.SafeMode && _startupStates.TryGetValue(manifest.Id, out StartupState? startupState) && startupState.Disabled)
				{
					state = PluginLifecycleState.Disabled;
					errorCode = PluginErrorCodes.StartupRecoveryDisabled;
				}
				if (!PluginManifestReader.IsPlatformSupported(manifest.Platforms))
				{
					state = PluginLifecycleState.Incompatible;
					errorCode = PluginErrorCodes.UnsupportedPlatform;
				}
				else if (!PluginManifestReader.IsCompatible(_options.HostApiVersion, manifest.Api))
				{
					state = PluginLifecycleState.Incompatible;
					errorCode = PluginErrorCodes.IncompatibleApi;
				}
				else if (!_options.DevelopmentHost && !PluginManifestReader.IsHostVersionSupported(_options.HostVersion, manifest.MinHostVersion))
				{
					state = PluginLifecycleState.Incompatible;
					errorCode = PluginErrorCodes.IncompatibleHost;
				}
				else if (manifest.Capabilities.Any(capability => !_options.KnownCapabilityIds.Contains(capability, StringComparer.Ordinal)))
				{
					state = PluginLifecycleState.Incompatible;
					errorCode = PluginErrorCodes.UnknownCapability;
				}
				Register(directory, manifest, state, errorCode);
			}
			catch (PluginException exception)
			{
				Report(exception);
			}
		}
		ValidateDependencies();
		return Plugins;
	}

	/// <summary>安装本地 .noripack。安全模式拒绝执行安装。</summary>
	public async Task<PluginManifest> InstallAsync(string packagePath, CancellationToken cancellationToken = default)
	{
		EnsureNotDisposed();
		if (_options.SafeMode) throw new PluginException(PluginErrorCodes.SafeModeDisabled, "安全模式下不允许安装插件");
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			PluginManifest manifest = await Task.Run(() => _installer.Install(packagePath, cancellationToken), cancellationToken).ConfigureAwait(false);
			Discover();
			return manifest;
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	/// <summary>同步安装入口，供安装命令之外的本地调用使用。</summary>
	public PluginManifest Install(string packagePath) => InstallAsync(packagePath).GetAwaiter().GetResult();

	/// <summary>返回依赖优先的插件快照顺序。</summary>
	public IReadOnlyList<PluginInfo> DependencyOrder()
	{
		EnsureNotDisposed();
		ValidateDependencies();
		List<PluginInfo> ordered = [];
		HashSet<string> visiting = new(StringComparer.Ordinal);
		HashSet<string> visited = new(StringComparer.Ordinal);
		foreach (PluginHandle handle in _plugins.Values.OrderBy(item => item.Manifest.Id, StringComparer.Ordinal))
		{
			if (handle.State is PluginLifecycleState.Discovered or PluginLifecycleState.Installed)
				Visit(handle, visiting, visited, ordered);
		}
		return ordered;
	}

	/// <summary>按依赖顺序激活全部可用插件。单个插件失败不会阻断宿主。</summary>
	public async Task StartAllAsync(CancellationToken cancellationToken = default)
	{
		EnsureNotDisposed();
		if (_options.SafeMode)
		{
			DisableAllThirdPartyPlugins();
			return;
		}
		IReadOnlyList<PluginInfo> ordered;
		try { ordered = DependencyOrder(); }
		catch (PluginException exception)
		{
			Report(exception);
			return;
		}
		foreach (PluginInfo info in ordered)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try { await ActivateAsync(info.Id, cancellationToken).ConfigureAwait(false); }
			catch (PluginException) { }
		}
	}

	/// <summary>加载并激活一个插件。</summary>
	public async Task ActivateAsync(string pluginId, CancellationToken cancellationToken = default)
	{
		EnsureNotDisposed();
		if (_options.SafeMode)
		{
			DisableAllThirdPartyPlugins();
			return;
		}
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (!_plugins.TryGetValue(pluginId, out PluginHandle? handle))
				throw new PluginException(PluginErrorCodes.InvalidManifest, $"未发现插件: {pluginId}");
			if (handle.State == PluginLifecycleState.Active) return;
			if (handle.State == PluginLifecycleState.Incompatible)
				throw new PluginException(handle.ErrorCode ?? PluginErrorCodes.IncompatibleHost, "插件与当前宿主不兼容");
			if (handle.State == PluginLifecycleState.Disabled)
				throw new PluginException(PluginErrorCodes.SafeModeDisabled, "插件已被禁用");
			ValidateDependenciesFor(handle);
			await ActivateCoreAsync(handle, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	/// <summary>停用一个插件并撤销所有贡献。</summary>
	public async Task DeactivateAsync(string pluginId, CancellationToken cancellationToken = default)
	{
		EnsureNotDisposed();
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (_plugins.TryGetValue(pluginId, out PluginHandle? handle))
			{
				try { await DeactivateCoreAsync(pluginId, cancellationToken, disabled: false).ConfigureAwait(false); }
				finally { UnloadContext(handle); }
			}
		}
		finally { _lifecycleGate.Release(); }
	}

	/// <summary>停用并卸载一个插件。</summary>
	public async Task UnloadAsync(string pluginId, CancellationToken cancellationToken = default)
	{
		EnsureNotDisposed();
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (!_plugins.TryGetValue(pluginId, out PluginHandle? handle)) return;
			try { await DeactivateCoreAsync(pluginId, cancellationToken, disabled: false).ConfigureAwait(false); }
			finally { UnloadContext(handle); }
		}
		finally { _lifecycleGate.Release(); }
	}

	/// <summary>禁用插件并保留安装文件与用户数据。</summary>
	public async Task DisableAsync(string pluginId, CancellationToken cancellationToken = default)
	{
		EnsureNotDisposed();
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (_plugins.TryGetValue(pluginId, out PluginHandle? handle))
			{
				try { await DeactivateCoreAsync(pluginId, cancellationToken, disabled: true).ConfigureAwait(false); }
				finally { UnloadContext(handle); }
			}
		}
		finally { _lifecycleGate.Release(); }
	}

	/// <summary>安全模式钩子：不创建 ALC、不调用任何第三方入口。</summary>
	public void DisableAllThirdPartyPlugins()
	{
		foreach (PluginHandle handle in _plugins.Values)
		{
			handle.State = PluginLifecycleState.Disabled;
			handle.ErrorCode = PluginErrorCodes.SafeModeDisabled;
		}
	}

	/// <summary>兼容宿主装配层的安全模式别名。</summary>
	public void DisableAllForSafeMode() => DisableAllThirdPartyPlugins();

	/// <summary>返回当前活动插件提供的指定类型贡献快照。</summary>
	public IReadOnlyList<T> GetContributions<T>()
		where T : class, IPluginContribution =>
		_plugins.Values.Where(handle => handle.State == PluginLifecycleState.Active)
			.SelectMany(handle => handle.Contributions.GetAll<T>().Select(contribution => PluginContributionProxies.Wrap(ToDescriptor(handle), contribution)))
			.ToArray();

	/// <summary>返回按 &lt;pluginId&gt;/&lt;toolId&gt; 组成的 Harness 工具快照。</summary>
	public IReadOnlyList<PluginHarnessTool> GetHarnessTools() =>
		_plugins.Values.Where(handle => handle.State == PluginLifecycleState.Active)
			.SelectMany(handle => handle.Contributions.GetAll<IHarnessTool>()
				.Select(tool => PluginContributionProxies.Wrap(ToDescriptor(handle), tool))
				.Select(tool => new PluginHarnessTool(HarnessToolIds.Compose(handle.Manifest.Id, tool.Descriptor.Id), tool)))
			.ToArray();

	/// <summary>返回当前插件的安装目录，供 AssetServer 做公开资源映射。</summary>
	public string? ResolveAssetRoot(string pluginId) =>
		_plugins.TryGetValue(pluginId, out PluginHandle? handle)
			? handle.Directory
			: _installer.ResolveCurrentDirectory(pluginId);

	public void MarkPendingRestart(string pluginId)
	{
		if (_plugins.TryGetValue(pluginId, out PluginHandle? handle)) handle.State = PluginLifecycleState.PendingRestart;
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		_shutdownSource.Cancel();
		foreach (string id in _plugins.Keys.ToArray())
		{
			bool acquired = false;
			try
			{
				acquired = await _lifecycleGate.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
				if (!acquired)
				{
					if (_plugins.TryGetValue(id, out PluginHandle? timedOutHandle)) timedOutHandle.State = PluginLifecycleState.PendingRestart;
					continue;
				}
				try
				{
					if (_plugins.TryGetValue(id, out PluginHandle? handle))
					{
						try { await DeactivateCoreAsync(id, CancellationToken.None, disabled: false).ConfigureAwait(false); }
						finally { UnloadContext(handle); }
					}
				}
				finally { if (acquired) _lifecycleGate.Release(); }
			}
			catch (Exception exception) when (exception is PluginException or TimeoutException or OperationCanceledException)
			{
				if (_plugins.TryGetValue(id, out PluginHandle? handle)) handle.State = PluginLifecycleState.PendingRestart;
			}
		}
		_shutdownSource.Dispose();
		_lifecycleGate.Dispose();
	}

	private async Task ActivateCoreAsync(PluginHandle handle, CancellationToken cancellationToken)
	{
		handle.State = PluginLifecycleState.Loading;
		PluginLoadContext? loadContext = null;
		try
		{
			PluginManifestReader.EnsureCompatible(_options.HostApiVersion, handle.Manifest.Api);
			if (!_options.DevelopmentHost && !PluginManifestReader.IsHostVersionSupported(_options.HostVersion, handle.Manifest.MinHostVersion))
				throw new PluginException(PluginErrorCodes.IncompatibleHost, "宿主版本低于插件要求");
			handle.Context = CreateContext(handle);
			ValidateRequiredCapabilities(handle.Context.CapabilityRegistry, handle.Manifest);
			INoriPlugin instance = _loader.Load(handle.Manifest, handle.Directory, out loadContext);

			handle.LoadContext = loadContext;
			handle.Instance = instance;
			await instance.ActivateAsync(handle.Context, cancellationToken).AsTask().WaitAsync(_options.ActivationTimeout, cancellationToken).ConfigureAwait(false);
			handle.State = PluginLifecycleState.Active;
			handle.ErrorCode = null;
			ClearStartupFailure(handle.Manifest.Id);
		}
		catch (PluginException exception)
		{
			FailActivation(handle, exception, loadContext);
			throw;
		}
		catch (Exception exception)
		{
			PluginException wrapped = new(PluginErrorCodes.ActivationFailed, $"插件激活失败: {handle.Manifest.Name}", exception);
			FailActivation(handle, wrapped, loadContext);
			throw wrapped;
		}
	}

	private async Task DeactivateCoreAsync(string pluginId, CancellationToken cancellationToken, bool disabled)
	{
		if (!_plugins.TryGetValue(pluginId, out PluginHandle? handle)) return;
		if (handle.Instance is null)
		{
			if (disabled) handle.State = PluginLifecycleState.Disabled;
			return;
		}
		handle.State = PluginLifecycleState.Stopping;
		try { handle.StopSource?.Cancel(throwOnFirstException: false); } catch { }
		PluginException? failure = null;
		try
		{
			await handle.Instance.DeactivateAsync(cancellationToken).AsTask().WaitAsync(_options.DeactivationTimeout, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			failure = exception is PluginException pluginException && pluginException.Code == PluginErrorCodes.DeactivationFailed
				? pluginException
				: new PluginException(PluginErrorCodes.DeactivationFailed, $"插件停用失败: {handle.Manifest.Name}", exception);
			Report(failure);
		}
		finally
		{
			handle.Context?.Revoke();
			handle.Instance = null;
			handle.Context?.Dispose();
			handle.Context = null;
			handle.StopSource = null;
			handle.State = failure is null ? (disabled ? PluginLifecycleState.Disabled : PluginLifecycleState.Installed) : PluginLifecycleState.Failed;
			handle.ErrorCode = failure?.Code;
		}
		if (failure is not null) throw failure;
	}

	private static PluginDescriptor ToDescriptor(PluginHandle handle) => new()
	{
		Id = handle.Manifest.Id,
		Name = handle.Manifest.Name,
		Description = handle.Manifest.Description,
		Version = handle.Manifest.Version,
		ApiVersion = handle.Manifest.ApiVersion,
		InstallPath = handle.Directory,
	};

	private PluginContext CreateContext(PluginHandle handle)
	{
		PluginDescriptor descriptor = ToDescriptor(handle);
		CancellationTokenSource stopSource = CancellationTokenSource.CreateLinkedTokenSource(_shutdownSource.Token);
		handle.StopSource = stopSource;
		IEnumerable<IPluginCapability> capabilities = _options.CapabilityFactory?.Invoke(descriptor, stopSource.Token) ?? [];
		PluginCapabilityRegistry capabilityRegistry = new(
			handle.Manifest.Capabilities.Concat(handle.Manifest.OptionalCapabilities),
			_options.KnownCapabilityIds,
			capabilities);
		return new PluginContext
		{
			Plugin = descriptor,
			Logger = new PluginLogger((message, exception) => _options.OnLog?.Invoke(descriptor, message, exception)),
			Storage = new JsonPluginStorage(Path.Combine(_options.DataDirectory, handle.Manifest.Id)),
			Assets = new PluginAssetProvider(handle.Directory, path => _options.AssetUriFactory?.Invoke(handle.Manifest.Id, path) ?? new Uri(Path.Combine(handle.Directory, path.Replace('/', Path.DirectorySeparatorChar)), UriKind.Absolute)),
			Contributions = handle.Contributions,
			Capabilities = capabilityRegistry,
			CapabilityRegistry = capabilityRegistry,
			ContributionRegistry = handle.Contributions,
			StoppingSource = stopSource,
		};
	}

	private static void ValidateRequiredCapabilities(PluginCapabilityRegistry registry, PluginManifest manifest)
	{
		foreach (PluginCapabilityStatus status in registry.Statuses.Where(status => manifest.Capabilities.Contains(status.Id, StringComparer.Ordinal)))
		{
			if (!status.Granted) throw new PluginException(PluginErrorCodes.CapabilityNotGranted, $"插件能力未获授权: {status.Id}");
			if (!status.Available) throw new PluginException(PluginErrorCodes.CapabilityUnavailable, $"插件能力当前不可用: {status.Id}");
		}
	}

	private void FailActivation(PluginHandle handle, PluginException exception, PluginLoadContext? loadContext)
	{
		handle.ErrorCode = exception.Code;
		handle.State = PluginLifecycleState.Failed;
		RecordStartupFailure(handle);
		handle.Context?.Revoke();
		handle.Context?.Dispose();
		if (handle.Context is null) handle.StopSource?.Dispose();
		handle.Context = null;
		handle.Instance = null;
		handle.StopSource = null;
		try { (loadContext ?? handle.LoadContext)?.Unload(); } catch { handle.State = PluginLifecycleState.PendingRestart; }
		handle.LoadContext = null;
		Report(exception);
	}

	private void UnloadContext(PluginHandle handle)
	{
		PluginLoadContext? context = handle.LoadContext;
		handle.LoadContext = null;
		if (context is null)
		{
			if (handle.State != PluginLifecycleState.Failed) handle.State = PluginLifecycleState.Installed;
			return;
		}
		WeakReference weak;
		try { weak = UnloadAndTrack(context); }
		catch (Exception exception)
		{
			handle.State = PluginLifecycleState.PendingRestart;
			handle.ErrorCode = PluginErrorCodes.UnloadPendingRestart;
			Report(new PluginException(PluginErrorCodes.UnloadPendingRestart, "插件程序集无法卸载", exception));
			return;
		}
		context = null;
		for (int index = 0; index < 10 && weak.IsAlive; index++)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			if (weak.IsAlive) Thread.Sleep(10);
		}
		if (weak.IsAlive)
		{
			handle.State = PluginLifecycleState.PendingRestart;
			handle.ErrorCode = PluginErrorCodes.UnloadPendingRestart;
		}
		else if (handle.State != PluginLifecycleState.Failed)
		{
			handle.State = PluginLifecycleState.Installed;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference UnloadAndTrack(PluginLoadContext context)
	{
		WeakReference weak = new(context);
		context.Unload();
		return weak;
	}

	private void Register(string directory, PluginManifest manifest, PluginLifecycleState state, string? errorCode)
	{
		if (_plugins.TryGetValue(manifest.Id, out PluginHandle? existing) && existing.State == PluginLifecycleState.Active)
		{
			existing.State = PluginLifecycleState.PendingRestart;
			existing.ErrorCode = null;
			return;
		}
		_plugins[manifest.Id] = new PluginHandle(directory, manifest, state, errorCode);
	}

	private void InstallInboxPackages()
	{
		foreach (string packagePath in Directory.EnumerateFiles(_installer.InboxDirectory, "*.noripack").Order(StringComparer.OrdinalIgnoreCase))
		{
			try
			{
				PluginManifest manifest = _installer.InspectPackage(packagePath);
				if (!_installer.IsVersionInstalled(manifest.Id, manifest.Version)) _installer.Install(packagePath);
			}
			catch (PluginException exception)
			{
				Report(exception);
			}
		}
	}

	private void ValidateDependencies()
	{
		foreach (PluginHandle handle in _plugins.Values)
		{
			if (handle.State is PluginLifecycleState.Disabled or PluginLifecycleState.Failed) continue;
			foreach (PluginDependency dependency in handle.Manifest.Dependencies)
			{
				if (!_plugins.TryGetValue(dependency.Id, out PluginHandle? target))
				{
					if (!dependency.Optional) SetIncompatible(handle, PluginErrorCodes.MissingDependency);
					continue;
				}
				if (!PluginRange.Satisfies(target.Manifest.PluginVersion, dependency.Version) && !dependency.Optional)
					SetIncompatible(handle, PluginErrorCodes.IncompatibleHost);
			}
		}
	}

	private void ValidateDependenciesFor(PluginHandle handle)
	{
		foreach (PluginDependency dependency in handle.Manifest.Dependencies)
		{
			if (!_plugins.TryGetValue(dependency.Id, out PluginHandle? target))
			{
				if (!dependency.Optional) throw new PluginException(PluginErrorCodes.MissingDependency, $"缺少插件依赖: {dependency.Id}");
				continue;
			}
			if (!PluginRange.Satisfies(target.Manifest.PluginVersion, dependency.Version) && !dependency.Optional)
				throw new PluginException(PluginErrorCodes.IncompatibleHost, $"插件依赖版本不满足: {dependency.Id}");
			if (!dependency.Optional && target.State != PluginLifecycleState.Active && !string.Equals(target.Manifest.Id, handle.Manifest.Id, StringComparison.Ordinal))
				throw new PluginException(PluginErrorCodes.MissingDependency, $"插件依赖尚未激活: {dependency.Id}");
		}
	}

	private void Visit(PluginHandle handle, HashSet<string> visiting, HashSet<string> visited, List<PluginInfo> ordered)
	{
		if (visited.Contains(handle.Manifest.Id)) return;
		if (!visiting.Add(handle.Manifest.Id)) throw new PluginException(PluginErrorCodes.DependencyCycle, $"插件依赖存在循环: {handle.Manifest.Id}");
		foreach (PluginDependency dependency in handle.Manifest.Dependencies.Where(item => !item.Optional))
		{
			if (!_plugins.TryGetValue(dependency.Id, out PluginHandle? target)) throw new PluginException(PluginErrorCodes.MissingDependency, $"缺少插件依赖: {dependency.Id}");
			if (target.State is PluginLifecycleState.Discovered or PluginLifecycleState.Installed) Visit(target, visiting, visited, ordered);
		}
		visiting.Remove(handle.Manifest.Id);
		visited.Add(handle.Manifest.Id);
		ordered.Add(handle.Info);
	}

	private void SetIncompatible(PluginHandle handle, string code)
	{
		if (handle.State is PluginLifecycleState.Active or PluginLifecycleState.Loading) return;
		handle.State = PluginLifecycleState.Incompatible;
		handle.ErrorCode = code;
	}

	private void RecordStartupFailure(PluginHandle handle)
	{
		if (!_startupStates.TryGetValue(handle.Manifest.Id, out StartupState? state)) state = new StartupState();
		state = state with { Failures = state.Failures + 1, Disabled = state.Failures + 1 >= 2 };
		_startupStates[handle.Manifest.Id] = state;
		PersistStartupStates();
		if (state.Disabled)
		{
			handle.State = PluginLifecycleState.Disabled;
			handle.ErrorCode = PluginErrorCodes.StartupRecoveryDisabled;
		}
	}

	private void ClearStartupFailure(string pluginId)
	{
		if (!_startupStates.Remove(pluginId)) return;
		PersistStartupStates();
	}

	private void PersistStartupStates()
	{
		string temporary = _startupStatePath + ".tmp-" + Guid.NewGuid().ToString("N");
		try
		{
			File.WriteAllText(temporary, JsonSerializer.Serialize(_startupStates));
			File.Move(temporary, _startupStatePath, true);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// 启动恢复记录失败不能阻断宿主；本次进程仍会隔离插件故障。
		}
		finally
		{
			try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
		}
	}

	private static Dictionary<string, StartupState> LoadStartupStates(string path)
	{
		if (!File.Exists(path)) return new(StringComparer.Ordinal);
		try
		{
			Dictionary<string, StartupState>? states = JsonSerializer.Deserialize<Dictionary<string, StartupState>>(File.ReadAllText(path));
			return states is null ? new(StringComparer.Ordinal) : new(states, StringComparer.Ordinal);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
		{
			return new(StringComparer.Ordinal);
		}
	}

	private void Report(PluginException exception)
	{
		try { _options.OnError?.Invoke(exception); } catch { }
	}

	private void EnsureNotDisposed()
	{
		if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(PluginManager));
	}

	private sealed record StartupState(int Failures = 0, bool Disabled = false);

	private sealed class PluginHandle
	{
		public PluginHandle(string directory, PluginManifest manifest, PluginLifecycleState state, string? errorCode)
		{
			Directory = directory;
			Manifest = manifest;
			State = state;
			ErrorCode = errorCode;
		}

		public string Directory { get; }
		public PluginManifest Manifest { get; }
		public PluginLifecycleState State { get; set; }
		public string? ErrorCode { get; set; }
		public INoriPlugin? Instance { get; set; }
		public PluginLoadContext? LoadContext { get; set; }
		public PluginContext? Context { get; set; }
		public CancellationTokenSource? StopSource { get; set; }
		public PluginContributionRegistry Contributions { get; } = new();
		public PluginInfo Info => new(Manifest.Id, Manifest, State, ErrorCode);
	}
}
