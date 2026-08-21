using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Nori.Core.Data;
using Nori.Desktop.Bridge;

namespace Nori.Desktop.Windows;

/// <summary>
/// 承载前端页面的窗口
///
/// 四个窗口共用这一个类, 差异全部来自 WindowDefinition. 每个窗口内含一个
/// NativeWebView, 加载同一份 Vue bundle, 靠 URL 上的 ?window=&lt;label&gt; 决定显示哪个页面.
/// </summary>
public sealed class NoriWindow : Window
{
	/// <summary>窗口标签</summary>
	public string Label { get; }

	/// <summary>
	/// 是否允许真正关闭
	///
	/// 平时关闭窗口只隐藏 (与 Tauri 版一致, 关窗不退应用), 只有窗口调度显式销毁时才放行
	/// </summary>
	public bool AllowClose { get; set; }

	private readonly NativeWebView _webView;
	private readonly NoriBridge _bridge;
	private bool _ready;
	private readonly List<string> _pendingScripts = [];
	private readonly object _inputMaskLock = new();
	private InputMask? _inputMask;
	private readonly Win32Properties.CustomWndProcHookCallback _inputMaskHook;

	[StructLayout(LayoutKind.Sequential)]
	private struct Rect
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetClientRect(nint hWnd, out Rect rect);

	[DllImport("user32.dll")]
	private static extern int SetWindowRgn(nint hWnd, nint hRgn, [MarshalAs(UnmanagedType.Bool)] bool redraw);

	[DllImport("gdi32.dll")]
	private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

	[DllImport("gdi32.dll")]
	private static extern int CombineRgn(nint destination, nint source1, nint source2, int mode);

	[DllImport("gdi32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DeleteObject(nint handle);

	public NoriWindow(WindowDefinition definition, NoriBridge bridge, string url)
	{
		Label = definition.Label;
		_bridge = bridge;
		_inputMaskHook = OnWndProc;

		Title = definition.Title;
		Width = definition.Width;
		Height = definition.Height;
		if (definition.MinWidth is { } minWidth) MinWidth = minWidth;
		if (definition.MinHeight is { } minHeight) MinHeight = minHeight;
		CanResize = definition.CanResize;
		Topmost = definition.Topmost;
		ShowInTaskbar = definition.ShowInTaskbar;
		// 无边框 + 逐像素透明: 圆角与桌宠去背都靠它
		WindowDecorations = WindowDecorations.None;
		Background = Brushes.Transparent;
		TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
		WindowStartupLocation = WindowStartupLocation.CenterScreen;
		Icon = LoadIcon();

		_webView = new NativeWebView
		{
			// 写入 WebView2 的 DefaultBackgroundColor, 桌宠窗口靠它透出桌面
			Background = Brushes.Transparent,
		};
		_webView.EnvironmentRequested += OnEnvironmentRequested;
		_webView.WebMessageReceived += OnWebMessageReceived;
		_webView.NavigationCompleted += OnNavigationCompleted;
		Content = _webView;

		_webView.Source = new Uri(url);
		Opened += OnOpened;
		Closed += OnClosed;

		// 窗口移动 / DPI 变化时主动推度量, 前端据此免掉每帧的位置与缩放往返
		PositionChanged += (_, _) => PostMetrics();
		ScalingChanged += (_, _) =>
		{
			PostMetrics();
			ApplyNativeInputMask();
		};
		SizeChanged += (_, _) => ApplyNativeInputMask();
	}

	/// <summary>
	/// 把当前窗口度量推给页面 (物理像素)
	/// </summary>
	public void PostMetrics()
	{
		double scale = RenderScaling;
		PostEvent("nori:window-metrics", new
		{
			label = Label,
			x = Position.X,
			y = Position.Y,
			width = (int)Math.Round((FrameSize?.Width ?? Bounds.Width) * scale),
			height = (int)Math.Round((FrameSize?.Height ?? Bounds.Height) * scale),
			scaleFactor = scale,
		});
	}

	/// <summary>
	/// 向该窗口的页面推送一个事件
	/// </summary>
	public void PostEvent(string name, object? payload)
	{
		string envelope = JsonSerializer.Serialize(new
		{
			kind = "event",
			@event = name,
			payload,
		}, BridgeJson.Options);
		Dispatch(envelope);
	}

	/// <summary>
	/// 回复一次 invoke 调用
	/// </summary>
	public void PostResult(long id, object? value, string? error)
	{
		string envelope = JsonSerializer.Serialize(error is null
			? new BridgeResult {Kind = "resolve", Id = id, Value = value}
			: new BridgeResult {Kind = "reject", Id = id, Error = error}, BridgeJson.Options);
		Dispatch(envelope);
	}

	/// <summary>
	/// 把 JSON 信封送进页面
	///
	/// 再序列化一次成 JS 字符串字面量, 页面里 JSON.parse 回来 —— 双层编码, 杜绝转义问题
	/// </summary>
	private void Dispatch(string envelopeJson)
	{
		string script = $"window.__nori&&window.__nori.dispatch({JsonSerializer.Serialize(envelopeJson)})";
		if (!Dispatcher.UIThread.CheckAccess())
		{
			Dispatcher.UIThread.Post(() => Dispatch(envelopeJson));
			return;
		}
		// 导航完成前发的事件先攒着, 否则页面还没定义 __nori
		if (!_ready)
		{
			_pendingScripts.Add(script);
			return;
		}
		_ = _webView.InvokeScript(script);
	}

	private void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
	{
		if (e is not WindowsWebView2EnvironmentRequestedEventArgs wv2) return;
		// 用户数据放到应用数据目录, 不要落在安装目录
		wv2.UserDataFolder = Path.Combine(AppPaths.DataDir, "webview");
	}

	private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
	{
		if (e.Body is not {Length: > 0} body) return;
		_bridge.Handle(this, body);
	}

	private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
	{
		if (!e.IsSuccess) return;
		_ready = true;
		foreach (string script in _pendingScripts) _ = _webView.InvokeScript(script);
		_pendingScripts.Clear();
		// 页面就绪后先给一份度量, 免得首次调用还要往返
		PostMetrics();
	}

	/// <summary>
	/// 窗口原生句柄, 供平台服务发起拖动
	/// </summary>
	public nint NativeHandle => TryGetPlatformHandle()?.Handle ?? 0;

	/// <summary>
	/// 更新桌宠窗口的低分辨率透明命中图.
	/// </summary>
	public void SetInputMask(int width, int height, string data, bool enabled)
	{
		InputMask? next = null;
		if (enabled)
		{
			if (width <= 0 || height <= 0 || width > 1024 || height > 1024)
				throw new InvalidOperationException("桌宠交互区域尺寸无效");
			byte[] bytes;
			try
			{
				bytes = Convert.FromBase64String(data);
			}
			catch (FormatException exception)
			{
				throw new InvalidOperationException("桌宠交互区域数据无效", exception);
			}
			int expected = checked((width * height + 7) / 8);
			if (bytes.Length != expected) throw new InvalidOperationException("桌宠交互区域数据长度无效");
			next = new InputMask(width, height, bytes);
		}
		lock (_inputMaskLock) _inputMask = next;
		ApplyNativeInputMask();
	}

	private void OnOpened(object? sender, EventArgs e)
	{
		if (OperatingSystem.IsWindows()) Win32Properties.AddWndProcHookCallback(this, _inputMaskHook);
		ApplyNativeInputMask();
	}

	private void OnClosed(object? sender, EventArgs e)
	{
		if (OperatingSystem.IsWindows()) Win32Properties.RemoveWndProcHookCallback(this, _inputMaskHook);
	}

	/// <summary>
	/// 用原生窗口区域裁掉透明单元。WebView2 是子 HWND，只处理父窗口 WM_NCHITTEST
	/// 不够，窗口区域才能让透明点真正落到桌面。
	/// </summary>
	private void ApplyNativeInputMask()
	{
		if (!OperatingSystem.IsWindows()) return;
		nint hWnd = NativeHandle;
		if (hWnd == 0 || !GetClientRect(hWnd, out Rect rect)) return;
		int windowWidth = rect.Right - rect.Left;
		int windowHeight = rect.Bottom - rect.Top;
		if (windowWidth <= 0 || windowHeight <= 0) return;

		InputMask? mask;
		lock (_inputMaskLock) mask = _inputMask;
		if (mask is null)
		{
			SetWindowRgn(hWnd, 0, true);
			return;
		}

		nint region = 0;
		try
		{
			for (int row = 0; row < mask.Height; row++)
			{
				int column = 0;
				while (column < mask.Width)
				{
					if (!mask.IsHit(column, row))
					{
						column++;
						continue;
					}
					int start = column++;
					while (column < mask.Width && mask.IsHit(column, row)) column++;
					nint part = CreateRectRgn(
						start * windowWidth / mask.Width,
						row * windowHeight / mask.Height,
						column * windowWidth / mask.Width,
						(row + 1) * windowHeight / mask.Height);
					if (part == 0) continue;
					if (region == 0)
					{
						region = part;
					}
					else
					{
						CombineRgn(region, region, part, 2 /* RGN_OR */);
						DeleteObject(part);
					}
				}
			}
			if (region == 0) region = CreateRectRgn(0, 0, 0, 0);
			if (SetWindowRgn(hWnd, region, true) == 0)
			{
				DeleteObject(region);
				region = 0;
			}
			else region = 0; // 成功后由 Windows 接管 HRGN 所有权
		}
		finally
		{
			if (region != 0) DeleteObject(region);
		}
	}

	private nint OnWndProc(nint hWnd, uint message, nint wParam, nint lParam, ref bool handled)
	{
		const uint WmNcHitTest = 0x0084;
		const nint HtTransparent = -1;
		if (message != WmNcHitTest || Label != WindowLabels.Pet) return 0;
		InputMask? mask;
		lock (_inputMaskLock) mask = _inputMask;
		if (mask is null) return 0;

		long packed = lParam.ToInt64();
		int screenX = unchecked((short)(packed & 0xffff));
		int screenY = unchecked((short)((packed >> 16) & 0xffff));
		int localX = screenX - Position.X;
		int localY = screenY - Position.Y;
		int windowWidth = Math.Max(1, (int)Math.Round((FrameSize?.Width ?? Bounds.Width) * RenderScaling));
		int windowHeight = Math.Max(1, (int)Math.Round((FrameSize?.Height ?? Bounds.Height) * RenderScaling));
		if (!mask.IsHit(localX, localY, windowWidth, windowHeight))
		{
			handled = true;
			return HtTransparent;
		}
		return 0;
	}

	private sealed class InputMask(int width, int height, byte[] data)
	{
		public int Width => width;
		public int Height => height;

		public bool IsHit(int column, int row) =>
			(data[row * width + column >> 3] & (1 << (row * width + column & 7))) != 0;

		public bool IsHit(int x, int y, int windowWidth, int windowHeight)
		{
			if (x < 0 || y < 0 || x >= windowWidth || y >= windowHeight) return false;
			int column = Math.Min(width - 1, x * width / windowWidth);
			int row = Math.Min(height - 1, y * height / windowHeight);
			int index = row * width + column;
			return (data[index >> 3] & (1 << (index & 7))) != 0;
		}
	}

	/// <summary>
	/// 加载窗口图标, 失败时返回 null 而不是让启动崩掉
	/// </summary>
	private static WindowIcon? LoadIcon()
	{
		try
		{
			return new WindowIcon(AssetLoader.Open(new Uri("avares://Nori.Desktop/Assets/icon.ico")));
		}
		catch (Exception exception) when (exception is FileNotFoundException or ArgumentException)
		{
			return null;
		}
	}
}
