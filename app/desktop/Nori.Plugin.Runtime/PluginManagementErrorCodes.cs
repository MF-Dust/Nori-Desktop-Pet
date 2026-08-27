namespace Nori.Plugin.Runtime;

/// <summary>插件管理垂直切片新增的稳定错误码。</summary>
public static class PluginManagementErrorCodes
{
	public const string InvalidPluginId = "plugin.invalid_id";
	public const string PluginNotFound = "plugin.not_found";
	public const string UserDisabled = "plugin.user_disabled";
	public const string DependencyInUse = "plugin.dependency_in_use";
	public const string UninstallPendingRestart = "plugin.uninstall_pending_restart";
}
