using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nori.Core.Platform;

/// <summary>
/// Windows 实现
///
/// 模型交互矩形由 PetWindow 的 WM_NCHITTEST 钩子逐点判定；SetClickThrough 同步
/// WS_EX_TRANSPARENT 与 Topmost Z 序，避免透明命中被困在 Topmost 窗口组内。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformServices : IPlatformServices
{
	private const uint WmNcLButtonDown = 0x00A1;
	private const nint HtCaption = 2;
	private const int GwlExStyle = -20;
	private const int WsExTransparent = 0x00000020;
	private static readonly nint HwndTopmost = new(-1);
	private static readonly nint HwndNoTopmost = new(-2);
	private const uint SwpNoSize = 0x0001;
	private const uint SwpNoMove = 0x0002;
	private const uint SwpNoActivate = 0x0010;
	private const uint SwpFrameChanged = 0x0020;

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetWindowPos(
		nint hWnd, nint hWndInsertAfter, int x, int y, int width, int height, uint flags);

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
		Marshal.SetLastPInvokeError(0);
		nint style = GetWindowLongPtr(windowHandle, GwlExStyle);
		int error = Marshal.GetLastPInvokeError();
		if (style == 0 && error != 0) throw new InvalidOperationException($"读取 Windows 桌宠窗口样式失败: {error}");

		nint next = through ? style | WsExTransparent : style & ~WsExTransparent;
		if (next != style)
		{
			Marshal.SetLastPInvokeError(0);
			nint previous = SetWindowLongPtr(windowHandle, GwlExStyle, next);
			error = Marshal.GetLastPInvokeError();
			if (previous == 0 && error != 0) throw new InvalidOperationException($"设置 Windows 桌宠窗口样式失败: {error}");
		}

		// Windows 不会让 HTTRANSPARENT/WS_EX_TRANSPARENT 跨 Topmost 组寻找普通窗口。
		// 穿透时降到普通 Z 序, 恢复可点时再抬回 Topmost, 不抢当前焦点。
		nint insertAfter = through ? HwndNoTopmost : HwndTopmost;
		if (!SetWindowPos(windowHandle, insertAfter, 0, 0, 0, 0,
			SwpNoSize | SwpNoMove | SwpNoActivate | SwpFrameChanged))
		{
			throw new InvalidOperationException($"设置 Windows 桌宠窗口层级失败: {Marshal.GetLastWin32Error()}");
		}
	}
}
