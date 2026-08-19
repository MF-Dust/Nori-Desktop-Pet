namespace Nori.Core.Platform;

/// <summary>
/// 非 Windows 平台的占位实现: 明确报错, 不静默降级
/// </summary>
public sealed class UnsupportedPlatformServices : IPlatformServices
{
	public bool IsSupported => false;

	public (double X, double Y) GetCursorPosition() =>
		throw new PlatformNotSupportedException("获取全局光标位置目前只支持 Windows");

	public bool IsMouseButtonDown(int button = 0) => false;

	public void StartWindowDrag(nint windowHandle) =>
		throw new PlatformNotSupportedException("原生窗口拖动目前只支持 Windows");
}
