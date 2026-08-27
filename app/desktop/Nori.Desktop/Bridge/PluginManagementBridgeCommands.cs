using System.Text.Json;
using Avalonia.Platform.Storage;
using Nori.Desktop.Windows;
using Nori.Plugin.Runtime;

namespace Nori.Desktop.Bridge;

/// <summary>宿主本地插件包选择器。前端从不提供任意文件系统路径。</summary>
public interface IPluginPackagePicker
{
	Task<string?> PickAsync(IBridgeSource source, CancellationToken cancellationToken = default);
}

public sealed class AvaloniaPluginPackagePicker(IUiDispatcher dispatcher) : IPluginPackagePicker
{
	private readonly IUiDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

	public Task<string?> PickAsync(IBridgeSource source, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		return _dispatcher.InvokeTaskAsync(async () =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			Avalonia.Controls.Window window = source.Self ?? throw new InvalidOperationException("来源窗口不可用");
			IReadOnlyList<IStorageFile> files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
			{
				Title = "选择 Nori 插件包 (.noripack)",
				AllowMultiple = false,
				FileTypeFilter =
				[
					new FilePickerFileType("Nori 插件包 (*.noripack)") { Patterns = ["*.noripack"] },
				],
			});
			cancellationToken.ThrowIfCancellationRequested();
			return files.Count == 0 ? null : files[0].Path.LocalPath;
		});
	}
}

/// <summary>
/// 插件管理命令域。它只接收主 WebView 的宿主请求，并直接委托 PluginManager。
/// PluginBridge 的插件侧白名单不参与这里的管理命令。
/// </summary>
public sealed class PluginManagementBridgeCommands
{
	private readonly AppServices _services;
	private readonly IPluginPackagePicker _picker;

	public PluginManagementBridgeCommands(AppServices services)
	{
		_services = services ?? throw new ArgumentNullException(nameof(services));
		_picker = services.PluginPackagePicker ?? new AvaloniaPluginPackagePicker(AvaloniaUiDispatcher.Instance);
	}

	public bool CanHandle(string command) => command.StartsWith("plugin_", StringComparison.Ordinal);

	public async Task<object?> InvokeAsync(
		IBridgeSource source,
		string command,
		JsonElement args,
		CancellationToken cancellationToken = default)
	{
		RequireVisibleMain(source);
		PluginManager manager = _services.Plugins ?? throw new InvalidOperationException("插件运行时尚未就绪");

		return command switch
		{
			"plugin_list" => List(manager),
			"plugin_install_local" => await InstallLocalAsync(manager, source, cancellationToken).ConfigureAwait(false),
			"plugin_enable" => await EnableAsync(manager, args, cancellationToken).ConfigureAwait(false),
			"plugin_disable" => await DisableAsync(manager, args, cancellationToken).ConfigureAwait(false),
			"plugin_uninstall" => await UninstallAsync(manager, args, cancellationToken).ConfigureAwait(false),
			_ => throw new InvalidOperationException($"未知插件管理命令: {command}"),
		};
	}

	private PluginListResultDto List(PluginManager manager)
	{
		IReadOnlyCollection<PluginInfo> plugins = manager.Discover();
		return new PluginListResultDto(plugins.OrderBy(item => item.Manifest.Name, StringComparer.OrdinalIgnoreCase).Select(ToDto).ToArray());
	}

	private async Task<PluginInstallResultDto> InstallLocalAsync(
		PluginManager manager,
		IBridgeSource source,
		CancellationToken cancellationToken)
	{
		if (_services.SafeMode) throw new PluginException(PluginErrorCodes.SafeModeDisabled, "安全模式下不允许安装插件");
		string? packagePath = await _picker.PickAsync(source, cancellationToken).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(packagePath)) return new PluginInstallResultDto(true, null);

		PluginManifest manifest = await manager.InstallAsync(packagePath, cancellationToken).ConfigureAwait(false);
		PluginInfo info = manager.Plugins.Single(item => string.Equals(item.Id, manifest.Id, StringComparison.Ordinal));
		return new PluginInstallResultDto(false, ToDto(info));
	}

	private async Task<PluginListItemDto> EnableAsync(PluginManager manager, JsonElement args, CancellationToken cancellationToken)
	{
		if (_services.SafeMode) throw new PluginException(PluginErrorCodes.SafeModeDisabled, "安全模式下不允许启用插件");
		string id = RequiredId(args);
		await manager.EnableAsync(id, cancellationToken).ConfigureAwait(false);
		return ToDto(Find(manager, id));
	}

	private async Task<PluginListItemDto> DisableAsync(PluginManager manager, JsonElement args, CancellationToken cancellationToken)
	{
		string id = RequiredId(args);
		await manager.DisableAsync(id, cancellationToken).ConfigureAwait(false);
		return ToDto(Find(manager, id));
	}

	private async Task<PluginUninstallResultDto> UninstallAsync(PluginManager manager, JsonElement args, CancellationToken cancellationToken)
	{
		string id = RequiredId(args);
		bool deleteData = OptionalBool(args, "deleteData");
		PluginUninstallResult result = await manager.UninstallAsync(id, deleteData, cancellationToken).ConfigureAwait(false);
		return new PluginUninstallResultDto(result.Success, result.RequiresRestart, result.Plugin is null ? null : ToDto(result.Plugin));
	}

	private PluginListItemDto Find(PluginManager manager, string id) =>
		manager.Plugins.Single(item => string.Equals(item.Id, id, StringComparison.Ordinal));

	private PluginListItemDto ToDto(PluginInfo info)
	{
		string author = string.Join(", ", info.Manifest.Authors.Select(item => item.Name.Trim()).Where(name => name.Length > 0));
		string? iconUrl = null;
		string? assetRoot = _services.Plugins?.ResolveAssetRoot(info.Id);
		if (_services.Assets is { } assets && assetRoot is not null && File.Exists(Path.Combine(assetRoot, "icon.png")))
			iconUrl = assets.PluginAssetUrl(info.Id, "icon.png");

		return new PluginListItemDto(
			info.Id,
			info.Manifest.Name,
			info.Manifest.Description,
			info.Manifest.Version,
			author,
			info.Manifest.Homepage,
			info.Manifest.Repository,
			info.Manifest.License,
			StateName(info.State),
			info.UserEnabled,
			info.Manifest.Capabilities.ToArray(),
			info.Manifest.OptionalCapabilities.ToArray(),
			info.CapabilityStatuses.Select(status => new PluginCapabilityStatusDto(status.Id, status.Declared, status.Granted, status.Available)).ToArray(),
			info.ErrorCode,
			info.ErrorMessage,
			info.RequiresRestart,
			iconUrl);
	}

	private static string StateName(PluginLifecycleState state) => state switch
	{
		PluginLifecycleState.Discovered or PluginLifecycleState.Installed => "installed",
		PluginLifecycleState.Loading => "loading",
		PluginLifecycleState.Active => "active",
		PluginLifecycleState.Stopping => "stopping",
		PluginLifecycleState.Disabled => "disabled",
		PluginLifecycleState.Failed => "failed",
		PluginLifecycleState.Incompatible => "incompatible",
		PluginLifecycleState.PendingRestart => "pending_restart",
		_ => "installed",
	};

	private static string RequiredId(JsonElement args)
	{
		if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("id", out JsonElement value) || value.ValueKind != JsonValueKind.String)
			throw new PluginException(PluginManagementErrorCodes.InvalidPluginId, "插件 ID 无效");
		string id = value.GetString() ?? string.Empty;
		if (!PluginManifestReader.IsValidPluginId(id))
			throw new PluginException(PluginManagementErrorCodes.InvalidPluginId, "插件 ID 无效");
		return id;
	}

	private static bool OptionalBool(JsonElement args, string name) =>
		args.ValueKind == JsonValueKind.Object
		&& args.TryGetProperty(name, out JsonElement value)
		&& value.ValueKind is JsonValueKind.True or JsonValueKind.False
		&& value.GetBoolean();

	private static void RequireVisibleMain(IBridgeSource source)
	{
		if (!string.Equals(source.Label, WindowLabels.Main, StringComparison.Ordinal) || !source.IsVisible)
			throw new UnauthorizedAccessException("插件管理仅允许可见的主窗口调用");
	}
}
