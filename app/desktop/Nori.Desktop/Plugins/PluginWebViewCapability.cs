using Nori.Plugin.Abstractions;

namespace Nori.Desktop.Plugins;

/// <summary>
/// Web 视图能力实现 (ui.webview)
///
/// 插件通过该能力向宿主申请创建独立的 Web 视图窗口。
/// 内部通过宿主注入的窗口工厂委托创建窗口，不向插件暴露 AppServices 或内部宿主状态。
/// </summary>
[PluginCapability("ui.webview")]
public sealed class PluginWebViewCapability : IWebViewCapability
{
	private readonly PluginDescriptorSummary _descriptor;
	private readonly Func<PluginDescriptorSummary, PluginWebViewOptions, CancellationToken, Task<IPluginWebViewWindow>> _windowFactory;

	public PluginWebViewCapability(
		PluginDescriptorSummary descriptor,
		Func<PluginDescriptorSummary, PluginWebViewOptions, CancellationToken, Task<IPluginWebViewWindow>> windowFactory)
	{
		_descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
		_windowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
	}

	/// <summary>
	/// 为插件创建独立的 Web 视图窗口
	/// </summary>
	public async Task<IPluginWebViewWindow> CreateWindowAsync(PluginWebViewOptions options, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(options);

		// 严格校验窗口 ID 命名规范
		PluginWindowHost.ValidateId(options.Id, nameof(options.Id));

		if (string.IsNullOrWhiteSpace(options.Title) || options.Title.Any(char.IsControl))
		{
			throw new ArgumentException("窗口标题不能为空。", nameof(options));
		}

		if (string.IsNullOrWhiteSpace(options.EntryPoint) || options.EntryPoint.Any(char.IsControl))
		{
			throw new ArgumentException("入口路径不能为空且不能包含控制字符。", nameof(options));
		}

		if (double.IsNaN(options.Width) || double.IsInfinity(options.Width) || options.Width <= 0 || double.IsNaN(options.Height) || double.IsInfinity(options.Height) || options.Height <= 0)
		{
			throw new ArgumentException("窗口宽度和高度必须大于 0。", nameof(options));
		}

		cancellationToken.ThrowIfCancellationRequested();
		return await _windowFactory(_descriptor, options, cancellationToken).ConfigureAwait(false);
	}
}
