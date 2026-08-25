using Nori.Core.Automation;

namespace Nori.Desktop.Automation.Windows;

/// <summary>顶层窗口枚举与安全目标校验。</summary>
public sealed class WindowsWindowService
{
	public static readonly nint PerMonitorAwareV2Context = new(-4);
	private readonly IWindowsWindowNativeApi _native;

	public WindowsWindowService(IWindowsWindowNativeApi? native = null) => _native = native ?? CreateNative();
	private static IWindowsWindowNativeApi CreateNative()
	{
		if (OperatingSystem.IsWindows()) return new Win32WindowNativeApi();
		return new UnsupportedWindowNativeApi();
	}
	public WindowsAutomationAvailability Availability => WindowsAutomationAvailability.Current;

	/// <summary>枚举当前桌面可见顶层窗口。</summary>
	public IReadOnlyList<WindowsTopLevelWindow> EnumerateTopLevelWindows()
	{
		if (!Availability.IsAvailable) return [];
		List<WindowsTopLevelWindow> result = [];
		_native.TryEnumerateTopLevelWindows(handle =>
		{
			if (_native.GetRootWindow(handle) != handle || !_native.IsWindowVisible(handle)) return true;
			if (!_native.TryGetWindowRect(handle, out WindowsNativeRect rect) || rect.Width <= 0 || rect.Height <= 0) return true;
			result.Add(new(handle, _native.GetWindowTitle(handle), _native.GetProcessId(handle), new(rect.Left, rect.Top, rect.Width, rect.Height), NormalizeDpi(_native.GetDpiForWindow(handle)), _native.GetForegroundWindow() == handle));
			return true;
		});
		return result;
	}

	/// <summary>校验目标；输入和截图默认要求目标仍为前台窗口。</summary>
	public WindowsTargetValidationResult ValidateTarget(nint handle, bool requireForeground = true)
	{
		if (!Availability.IsAvailable) return new(false, WindowsTargetRejection.UnsupportedPlatform, Availability.Reason);
		if (handle == 0 || !_native.IsWindow(handle)) return new(false, WindowsTargetRejection.InvalidHandle, "目标窗口句柄无效");
		if (_native.GetRootWindow(handle) != handle) return new(false, WindowsTargetRejection.NotTopLevel, "目标必须是顶层窗口");
		if (!_native.IsWindowVisible(handle)) return new(false, WindowsTargetRejection.NotVisible, "目标窗口不可见");
		if (_native.IsSecureDesktop()) return new(false, WindowsTargetRejection.SecureDesktop, "安全桌面上禁止桌面自动化");
		if (!_native.IsUipiAllowed(handle)) return new(false, WindowsTargetRejection.UipiBlocked, "目标窗口受到 UIPI 完整性级别限制");
		if (requireForeground && _native.GetForegroundWindow() != handle) return new(false, WindowsTargetRejection.NotForeground, "目标窗口不是当前前台窗口");
		if (!_native.TryGetWindowRect(handle, out WindowsNativeRect rect) || rect.Width <= 0 || rect.Height <= 0) return new(false, WindowsTargetRejection.InvalidBounds, "目标窗口区域无效");
		return WindowsTargetValidationResult.Valid;
	}

	public bool TryGetBounds(nint handle, out WindowsNativeRect rect) => _native.TryGetWindowRect(handle, out rect);
	public uint GetDpi(nint handle) => NormalizeDpi(_native.GetDpiForWindow(handle));
	private static uint NormalizeDpi(uint dpi) => dpi == 0 ? 96u : dpi;

	/// <summary>在 Per-Monitor V2 上下文中读取物理像素区域。</summary>
	public T WithPerMonitorDpi<T>(Func<T> action)
	{
		if (!Availability.IsAvailable) return action();
		nint previous = _native.SetThreadDpiAwarenessContext(PerMonitorAwareV2Context);
		if (previous == 0) throw new InvalidOperationException("无法启用 Per-Monitor DPI 感知");
		try { return action(); }
		finally { _native.SetThreadDpiAwarenessContext(previous); }
	}
}
