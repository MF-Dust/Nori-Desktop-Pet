using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nori.Core.Platform;

/// <summary>
/// Windows 实现
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformServices : IPlatformServices
{
	private const uint WmNcLButtonDown = 0x00A1;
	private const nint HtCaption = 2;

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

	public bool IsSupported => true;

	public (double X, double Y) GetCursorPosition()
	{
		if (!GetCursorPos(out Point point)) throw new InvalidOperationException("GetCursorPos 调用失败");
		return (point.X, point.Y);
	}

	public void StartWindowDrag(nint windowHandle)
	{
		if (windowHandle == 0) throw new ArgumentException("窗口句柄无效", nameof(windowHandle));
		// 先释放鼠标捕获, 再让系统把这次按下当作拖标题栏处理
		ReleaseCapture();
		SendMessage(windowHandle, WmNcLButtonDown, HtCaption, 0);
	}
}
