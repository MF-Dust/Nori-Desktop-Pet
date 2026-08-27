using System.Reflection;
using Nori.Plugin.Abstractions;

namespace Nori.Plugin.Runtime;

/// <summary>插件运行时配置。</summary>
public sealed record PluginRuntimeOptions
{
	public required string PluginsDirectory { get; init; }
	public required string DataDirectory { get; init; }
	public PluginVersion HostSchemaVersion { get; init; } = new(1, 0, 0);
	public PluginVersion HostApiVersion { get; init; } = new(1, 0, 0);
	public bool SafeMode { get; init; }
	public Action<PluginException>? OnError { get; init; }
}

/// <summary>已发现插件的状态快照。</summary>
public sealed record PluginInfo(string Id, PluginManifest Manifest, PluginLifecycleState State, string? ErrorCode);

/// <summary>插件生命周期管理器。</summary>
public sealed class PluginManager : IAsyncDisposable
{
	private readonly PluginRuntimeOptions _options;
	private readonly PluginPackageInstaller _installer;
	private readonly Dictionary<string, PluginHandle> _plugins = new(StringComparer.Ordinal);
	private readonly PluginUiProviderRegistry _uiProviders = new();
	private int _disposed;

	public PluginManager(PluginRuntimeOptions options)
	{
		_options = options;
		Directory.CreateDirectory(options.PluginsDirectory);
		Directory.CreateDirectory(options.DataDirectory);
		_installer = new PluginPackageInstaller(options.PluginsDirectory);
	}

	public IReadOnlyCollection<PluginInfo> Plugins => _plugins.Values.Select(plugin => plugin.Info).ToArray();
	public PluginPackageInstaller Installer => _installer;
	public IPluginUiProviderRegistry UiProviders => _uiProviders;

	/// <summary>扫描 current 目录，只读取和校验清单；安全模式不会加载程序集。</summary>
	public IReadOnlyCollection<PluginInfo> Discover()
	{
		EnsureNotDisposed();
		string currentRoot = Path.Combine(_options.PluginsDirectory, "current");
		if (!Directory.Exists(currentRoot)) return Plugins;
		foreach (string directory in Directory.EnumerateDirectories(currentRoot))
		{
			string manifestPath = Path.Combine(directory, PluginPackageInstaller.ManifestFileName);
			if (!File.Exists(manifestPath)) manifestPath = Path.Combine(directory, PluginPackageInstaller.LegacyManifestFileName);
			try
			{
				PluginManifest manifest = PluginManifestReader.Read(manifestPath);
				if (!string.Equals(Path.GetFileName(directory), manifest.PluginId, StringComparison.Ordinal))
					throw new PluginException(PluginErrorCodes.InvalidManifest, "插件目录 ID 与清单不一致");
				RegisterDiscovered(directory, manifest);
			}
			catch (PluginException exception) { Report(exception); }
		}
		ValidateDependencies();
		return Plugins;
	}

	public PluginManifest Install(string packagePath)
	{
		EnsureNotDisposed();
		if (_options.SafeMode) throw new PluginException(PluginErrorCodes.LifecycleFailed, "安全模式下不允许安装插件");
		PluginManifest manifest = _installer.Install(packagePath);
		Discover();
		return manifest;
	}

	public IReadOnlyList<PluginInfo> DependencyOrder()
	{
		ValidateDependencies();
		List<PluginInfo> ordered = [];
		HashSet<string> visiting = new(StringComparer.Ordinal);
		HashSet<string> visited = new(StringComparer.Ordinal);
		foreach (PluginHandle plugin in _plugins.Values) Visit(plugin, visiting, visited, ordered);
		return ordered;
	}

	public async Task StartAllAsync(CancellationToken cancellationToken = default)
	{
		EnsureNotDisposed();
		if (_options.SafeMode) { DisableAllForSafeMode(); return; }
		foreach (PluginInfo info in DependencyOrder())
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (_plugins[info.Id].State != PluginLifecycleState.Discovered) continue;
			try { await LoadAndStartAsync(info.Id, cancellationToken).ConfigureAwait(false); }
			catch (PluginException) { /* 单个插件故障不能阻塞其他插件与宿主启动 */ }
		}
	}

	public async Task LoadAndStartAsync(string pluginId, CancellationToken cancellationToken = default)
	{
		EnsureNotDisposed();
		if (_options.SafeMode) { DisableAllForSafeMode(); return; }
		if (!_plugins.TryGetValue(pluginId, out PluginHandle? handle)) throw new PluginException(PluginErrorCodes.InvalidManifest, $"未发现插件: {pluginId}");
		if (handle.State is PluginLifecycleState.Active) return;
		try
		{
			handle.State = PluginLifecycleState.Loading;
			PluginVersion pluginSchema = handle.Manifest.Schema;
			PluginVersion pluginApi = handle.Manifest.Api;
			PluginManifestReader.EnsureCompatible(_options.HostSchemaVersion, pluginSchema, _options.HostApiVersion, pluginApi);
			string assemblyPath = ResolveEntryAssembly(handle);
			PluginLoadContext.EnsureReferencesAllowed(handle.Directory);
			PluginLoadContext loadContext = new(assemblyPath);
			Assembly assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
			Type? type = assembly.GetType(handle.Manifest.EntryType, throwOnError: false, ignoreCase: false);
			if (type is null || !typeof(INoriPlugin).IsAssignableFrom(type) || type.IsAbstract)
				throw new PluginException(PluginErrorCodes.EntryTypeNotFound, $"入口类型不存在或不是 INoriPlugin: {handle.Manifest.EntryType}");
			if (Activator.CreateInstance(type) is not INoriPlugin instance)
				throw new PluginException(PluginErrorCodes.EntryTypeNotFound, "插件入口无法创建");
			handle.LoadContext = loadContext;
			handle.Instance = instance;
			PluginContext context = CreateContext(handle);
			await instance.StartAsync(context, cancellationToken).ConfigureAwait(false);
			handle.State = PluginLifecycleState.Active;
		}
		catch (PluginException exception) { Fail(handle, exception); throw; }
		catch (Exception exception)
		{
			PluginException wrapped = new(PluginErrorCodes.LifecycleFailed, $"插件启动失败: {handle.Manifest.Name}", exception);
			Fail(handle, wrapped);
			throw wrapped;
		}
	}

	public async Task StopAsync(string pluginId, CancellationToken cancellationToken = default)
	{
		if (!_plugins.TryGetValue(pluginId, out PluginHandle? handle) || handle.Instance is null) return;
		try
		{
			await handle.Instance.StopAsync(cancellationToken).ConfigureAwait(false);
			handle.State = PluginLifecycleState.Unloaded;
		}
		catch (Exception exception)
		{
			PluginException wrapped = new(PluginErrorCodes.LifecycleFailed, $"插件停止失败: {handle.Manifest.Name}", exception);
			Fail(handle, wrapped);
			throw wrapped;
		}
	}

	public async Task UnloadAsync(string pluginId, CancellationToken cancellationToken = default)
	{
		if (!_plugins.TryGetValue(pluginId, out PluginHandle? handle)) return;
		try { await StopAsync(pluginId, cancellationToken).ConfigureAwait(false); }
		finally
		{
			handle.Instance = null;
			handle.LoadContext?.Unload();
			handle.LoadContext = null;
			if (handle.State != PluginLifecycleState.Faulted) handle.State = PluginLifecycleState.Unloaded;
		}
	}

	public void MarkPendingRestart(string pluginId)
	{
		if (_plugins.TryGetValue(pluginId, out PluginHandle? handle)) handle.State = PluginLifecycleState.PendingRestart;
	}

	public void DisableAllForSafeMode()
	{
		foreach (PluginHandle handle in _plugins.Values)
		{
			handle.State = PluginLifecycleState.Disabled;
			handle.Instance = null;
			handle.LoadContext = null;
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		foreach (string id in _plugins.Keys.ToArray())
		{
			try { await UnloadAsync(id).ConfigureAwait(false); } catch { }
		}
	}

	private void RegisterDiscovered(string directory, PluginManifest manifest)
	{
		if (_plugins.TryGetValue(manifest.PluginId, out PluginHandle? existing))
		{
			if (string.Equals(existing.Manifest.Version, manifest.Version, StringComparison.Ordinal)) return;
			if (existing.State == PluginLifecycleState.Active)
			{
				existing.State = PluginLifecycleState.PendingRestart;
				return;
			}
			_plugins.Remove(manifest.PluginId);
		}
		PluginHandle handle = new(manifest, directory) { State = _options.SafeMode ? PluginLifecycleState.Disabled : PluginLifecycleState.Discovered };
		_plugins.Add(manifest.PluginId, handle);
	}

	private void ValidateDependencies()
	{
		foreach (PluginHandle plugin in _plugins.Values)
			foreach (PluginDependency dependency in plugin.Manifest.Dependencies)
			{
				if (!_plugins.TryGetValue(dependency.PluginId, out PluginHandle? dependencyPlugin))
					throw new PluginException(PluginErrorCodes.MissingDependency, $"插件缺少依赖: {plugin.Manifest.PluginId} -> {dependency.PluginId}");
				if (PluginVersion.Parse(dependencyPlugin.Manifest.Version).CompareTo(PluginVersion.Parse(dependency.MinVersion)) < 0)
					throw new PluginException(PluginErrorCodes.IncompatibleVersion, $"插件依赖版本过低: {plugin.Manifest.PluginId} -> {dependency.PluginId}");
			}
	}

	private void Visit(PluginHandle plugin, HashSet<string> visiting, HashSet<string> visited, List<PluginInfo> ordered)
	{
		if (visited.Contains(plugin.Manifest.PluginId)) return;
		if (!visiting.Add(plugin.Manifest.PluginId)) throw new PluginException(PluginErrorCodes.DependencyCycle, $"插件依赖存在循环: {plugin.Manifest.PluginId}");
		foreach (PluginDependency dependency in plugin.Manifest.Dependencies) Visit(_plugins[dependency.PluginId], visiting, visited, ordered);
		visiting.Remove(plugin.Manifest.PluginId);
		visited.Add(plugin.Manifest.PluginId);
		ordered.Add(plugin.Info);
	}

	private string ResolveEntryAssembly(PluginHandle handle)
	{
		string path = Path.GetFullPath(Path.Combine(handle.Directory, "lib", handle.Manifest.EntryAssembly));
		if (!File.Exists(path)) throw new PluginException(PluginErrorCodes.EntryTypeNotFound, $"入口程序集不存在: {handle.Manifest.EntryAssembly}");
		return path;
	}

	private PluginContext CreateContext(PluginHandle handle) => new()
	{
		PluginId = handle.Manifest.PluginId,
		DataDirectory = Path.Combine(_options.DataDirectory, handle.Manifest.PluginId),
		Storage = new JsonPluginStorage(Path.Combine(_options.DataDirectory, handle.Manifest.PluginId)),
		Assets = new PluginAssetReader(handle.Directory),
		Contributions = handle.Contributions,
		Capabilities = handle.Capabilities,
		UiProviders = _uiProviders,
		ShutdownToken = CancellationToken.None,
	};

	private void Fail(PluginHandle handle, PluginException exception)
	{
		handle.State = PluginLifecycleState.Faulted;
		handle.ErrorCode = exception.Code;
		handle.Instance = null;
		handle.LoadContext?.Unload();
		handle.LoadContext = null;
		Report(exception);
	}

	private void Report(PluginException exception) { try { _options.OnError?.Invoke(exception); } catch { } }
	private void EnsureNotDisposed() { if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(PluginManager)); }

	private sealed class PluginHandle
	{
		public PluginHandle(PluginManifest manifest, string directory) { Manifest = manifest; Directory = directory; }
		public PluginManifest Manifest { get; }
		public string Directory { get; }
		public PluginLifecycleState State { get; set; }
		public string? ErrorCode { get; set; }
		public INoriPlugin? Instance { get; set; }
		public PluginLoadContext? LoadContext { get; set; }
		public PluginContributionRegistry Contributions { get; } = new();
		public PluginCapabilityRegistry Capabilities { get; } = new();
		public PluginInfo Info => new(Manifest.PluginId, Manifest, State, ErrorCode);
	}
}
