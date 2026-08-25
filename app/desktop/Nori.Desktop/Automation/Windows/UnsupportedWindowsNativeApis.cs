using Nori.Core.Automation;

namespace Nori.Desktop.Automation.Windows;

/// <summary>非 Windows 平台的明确拒绝实现。</summary>
internal sealed class UnsupportedWindowNativeApi : IWindowsWindowNativeApi
{
	public bool TryEnumerateTopLevelWindows(Func<nint, bool> callback) => false;
	public bool IsWindow(nint handle) => false;
	public bool IsWindowVisible(nint handle) => false;
	public nint GetRootWindow(nint handle) => 0;
	public string GetWindowTitle(nint handle) => "";
	public int GetProcessId(nint handle) => 0;
	public nint GetForegroundWindow() => 0;
	public bool TryGetWindowRect(nint handle, out WindowsNativeRect rect) { rect = default; return false; }
	public uint GetDpiForWindow(nint handle) => 0;
	public bool IsSecureDesktop() => true;
	public bool IsUipiAllowed(nint handle) => false;
	public nint SetThreadDpiAwarenessContext(nint context) => 0;
}

internal sealed class UnsupportedInputNativeApi : IWindowsInputNativeApi
{
	public bool TryGetVirtualScreenBounds(out AutomationBounds bounds) { bounds = default; return false; }
	public bool TrySendInput(nint target, IReadOnlyList<WindowsInputPacket> packets, out WindowsInputSendFailure failure) { failure = new(0, "Windows 桌面自动化仅支持 Windows"); return false; }
}

internal sealed class UnsupportedCaptureNativeApi : IWindowsScreenCaptureNativeApi
{
	public bool TryCaptureWindow(nint handle, WindowsNativeRect rect, out byte[]? bgra32, out string? error) { bgra32 = null; error = "Windows 桌面自动化仅支持 Windows"; return false; }
}
