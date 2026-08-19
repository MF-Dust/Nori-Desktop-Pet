namespace Nori.Core.Platform;

/// <summary>
/// 平台相关能力
///
/// 浏览器拿不到窗口外的光标, 也无法从 WebView 内部发起原生窗口拖动,
/// 这两件事必须由宿主用系统 API 完成. 所有 Win32 调用收敛在这个接口之后,
/// 将来移植 macOS / Linux 时只需新增一个实现.
/// </summary>
public interface IPlatformServices
{
	/// <summary>当前平台是否支持这些能力</summary>
	bool IsSupported { get; }

	/// <summary>
	/// 获取全局光标位置 (物理像素, 相对屏幕左上角)
	/// </summary>
	(double X, double Y) GetCursorPosition();

	/// <summary>
	/// 从当前鼠标按下状态发起窗口拖动
	///
	/// WebView 会吞掉指针事件, 因此 HTML 标题栏的拖动要回调到宿主由系统接管
	/// </summary>
	void StartWindowDrag(nint windowHandle);
}

/// <summary>
/// 平台能力入口
/// </summary>
public static class PlatformServices
{
	/// <summary>
	/// 当前平台的实现
	/// </summary>
	public static IPlatformServices Current { get; } = OperatingSystem.IsWindows()
		? new WindowsPlatformServices()
		: new UnsupportedPlatformServices();
}
