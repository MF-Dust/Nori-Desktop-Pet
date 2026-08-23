using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nori.Core.Platform;

/// <summary>
/// Windows 实现
///
/// 逐像素穿透在 Windows 上由 PetWindow 的 WM_NCHITTEST 钩子逐点判定 (精度最好),
/// 因此 SetClickThrough 只在需要整窗穿透时兜底改 WS_EX_TRANSPARENT。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformServices : IPlatformServices
{
	private const uint WmNcLButtonDown = 0x00A1;
	private const nint HtCaption = 2;
	private const int GwlExStyle = -20;
	private const int WsExTransparent = 0x00000020;

	[StructLayout(LayoutKind.Sequential)]
	private struct Point
	{
		public int X;
		public int Y;
	}

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetCursorPos(out Point point);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool ReleaseCapture();

	[DllImport("user32.dll", EntryPoint = "SendMessageW")]
	private static extern nint SendMessage(nint hWnd, uint message, nint wParam, nint lParam);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
	private static extern nint GetWindowLongPtr(nint hWnd, int index);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
	private static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);

	/// <inheritdoc />
	public SessionType Session => SessionType.Windows;

	/// <inheritdoc />
	public PlatformCapabilities Capabilities { get; } = new()
	{
		SupportsGlobalCursor = true,
		SupportsWindowDrag = true,
		SupportsHitThrough = true,
		SupportsTopmost = true,
		SupportsTray = true,
	};

	/// <inheritdoc />
	public (double X, double Y) GetCursorPosition()
	{
		if (!GetCursorPos(out Point point)) return (0, 0);
		return (point.X, point.Y);
	}

	/// <inheritdoc />
	public void StartWindowDrag(nint windowHandle)
	{
		if (windowHandle == 0) throw new ArgumentException("窗口句柄无效", nameof(windowHandle));
		// 先释放鼠标捕获, 再让系统把这次按下当作拖标题栏处理
		ReleaseCapture();
		SendMessage(windowHandle, WmNcLButtonDown, HtCaption, 0);
	}

	/// <inheritdoc />
	public void SetClickThrough(nint windowHandle, bool through)
	{
		if (windowHandle == 0) return;
		nint style = GetWindowLongPtr(windowHandle, GwlExStyle);
		nint next = through ? style | WsExTransparent : style & ~WsExTransparent;
		if (next != style) SetWindowLongPtr(windowHandle, GwlExStyle, next);
	}
}
