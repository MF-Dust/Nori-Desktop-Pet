namespace Nori.Plugin.Abstractions;

/// <summary>
/// 声明插件能力的特性标记
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class PluginCapabilityAttribute(string name) : Attribute
{
	/// <summary>能力标识符 (例如: ui.webview, storage, ai)</summary>
	public string Name { get; } = name;
}

/// <summary>
/// Web 视图能力契约 (ui.webview)
///
/// 插件通过该能力向宿主申请创建独立的 Web 视图窗口。
/// </summary>
public interface IWebViewCapability
{
	/// <summary>
	/// 为插件创建独立的 Web 视图窗口
	/// </summary>
	Task<IPluginWebViewWindow> CreateWindowAsync(PluginWebViewOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// 插件 Web 视图窗口句柄抽象
/// </summary>
public interface IPluginWebViewWindow : IAsyncDisposable
{
	/// <summary>所属插件 ID</summary>
	string PluginId { get; }

	/// <summary>窗口 ID (在所属插件域内唯一)</summary>
	string WindowId { get; }

	/// <summary>全局唯一窗口标签 (格式: plugin:{pluginId}:{windowId})</summary>
	string Label { get; }

	/// <summary>窗口标题</summary>
	string? Title { get; }

	/// <summary>窗口当前是否可见</summary>
	bool IsVisible { get; }

	/// <summary>显示并聚焦窗口</summary>
	Task ShowAsync(CancellationToken cancellationToken = default);

	/// <summary>隐藏窗口</summary>
	Task HideAsync(CancellationToken cancellationToken = default);

	/// <summary>关闭窗口并释放相关资源</summary>
	Task CloseAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 插件 Web 视图窗口创建参数
/// </summary>
public sealed record PluginWebViewOptions
{
	/// <summary>窗口标识 (插件域内唯一, 仅允许字母、数字、下划线、中划线、点)</summary>
	public required string WindowId { get; init; }

	/// <summary>窗口标题</summary>
	public required string Title { get; init; }

	/// <summary>入口 URL (相对路径或受信任的同源 URL)</summary>
	public required string EntryUrl { get; init; }

	/// <summary>窗口宽度 (DIP)</summary>
	public double Width { get; init; } = 800;

	/// <summary>窗口高度 (DIP)</summary>
	public double Height { get; init; } = 600;

	/// <summary>最小宽度</summary>
	public double? MinWidth { get; init; }

	/// <summary>最小高度</summary>
	public double? MinHeight { get; init; }

	/// <summary>是否允许用户调整尺寸</summary>
	public bool CanResize { get; init; } = true;

	/// <summary>是否置顶显示</summary>
	public bool Topmost { get; init; }

	/// <summary>是否在任务栏显示</summary>
	public bool ShowInTaskbar { get; init; } = true;
}

/// <summary>
/// 插件系统领域异常基类
/// </summary>
public class PluginException : Exception
{
	public PluginException(string message) : base(message) { }
	public PluginException(string message, Exception? innerException) : base(message, innerException) { }
}
