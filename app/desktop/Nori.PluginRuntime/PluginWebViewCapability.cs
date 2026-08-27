namespace Nori.PluginRuntime;

/// <summary>
/// Web 视图能力实现 (ui.webview)
///
/// 插件通过该能力向宿主申请创建独立的 Web 视图窗口。
/// 内部通过宿主注入的窗口工厂委托创建窗口，不向插件暴露 AppServices 或内部宿主状态。
/// </summary>
[PluginCapability("ui.webview")]
internal sealed class PluginWebViewCapability : IWebViewCapability
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
		PluginWindowHost.ValidatePluginId(_descriptor.Id, nameof(_descriptor.Id));
		PluginWindowOptionsValidator.Validate(options);

		cancellationToken.ThrowIfCancellationRequested();
		return await _windowFactory(_descriptor, options, cancellationToken).ConfigureAwait(false);
	}
}
