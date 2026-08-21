using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Live2DCSharpSDK.App;
using Live2DCSharpSDK.Framework;
using Live2DCSharpSDK.OpenGL;

namespace Nori.Desktop.Live2D;

/// <summary>
/// Avalonia 原生 OpenGL 渲染控件
///
/// 承载 Live2DCSharpSDK 原生渲染管线：
/// - 基于 OpenGlControlBase，以物理像素（Bounds x RenderScaling）渲染
/// - 开启 2048x2048 高精度裁剪蒙版缓冲与各向异性过滤
/// - 后台定时驱动 RequestNextFrameRendering，按 l2d_max_fps 限帧
/// - 约 10Hz 采样全视口 alpha 缓冲，提供高精度且无阻塞的 WM_NCHITTEST 命中测试
/// </summary>
public sealed class PetGlControl : OpenGlControlBase
{
	private const int MaskWidth = 96;
	private const int MaskHeight = 128;

	private readonly PetRuntime _runtime;
	private LAppDelegateOpenGL? _lapp;
	private AvaloniaGlApi? _glApi;
	private DateTime _lastRenderTime;

	// Alpha 命中掩码缓存
	private readonly object _maskLock = new();
	private readonly byte[] _maskBits = new byte[(MaskWidth * MaskHeight + 7) / 8];
	private readonly byte[] _maskScratch = new byte[(MaskWidth * MaskHeight + 7) / 8];
	private double _lastMaskSampleTime;
	private int _lastViewportW;
	private int _lastViewportH;
	private byte[] _pixelBuffer = [];

	// 帧驱动
	private CancellationTokenSource? _fpsCts;
	private Thread? _renderThread;
	/// <summary>渲染线程是否在运行 (窗口隐藏时暂停, 避免不可见空转)</summary>
	private volatile bool _renderLoopRunning;
	/// <summary>已排队但尚未渲染的帧请求, 用于避免 Dispatcher 队列堆积</summary>
	private int _framePending;

	public PetGlControl(PetRuntime runtime)
	{
		_runtime = runtime;
	}

	protected override void OnOpenGlInit(GlInterface gl)
	{
		base.OnOpenGlInit(gl);

		// Cubism 的日志绝不能走 Console.WriteLine: Nori.Desktop 是 WinExe, 没有控制台,
		// Console.WriteLine 会抛 IOException(句柄无效), 而它是在渲染回调里被调用的,
		// 未捕获会直接把整个进程带走。统一转到应用自己的文件日志。
		var cubismAllocator = new LAppAllocator();
		var cubismOption = new CubismOption
		{
			LogFunction = _runtime.WriteCubismLog,
			LoggingLevel = LogLevel.Warning,
		};
		CubismFramework.StartUp(cubismAllocator, cubismOption);

		_glApi = new AvaloniaGlApi(this, gl);
		_lapp = new LAppDelegateOpenGL(_glApi)
		{
			BGColor = new(0, 0, 0, 0),
		};

		_runtime.OnGlInit(_lapp, _glApi);
		StartRenderLoop();
	}

	protected override void OnOpenGlDeinit(GlInterface gl)
	{
		StopRenderLoop();
		_runtime.OnGlDeinit();
		_lapp?.Dispose();
		_lapp = null;
		_glApi = null;
		CubismFramework.CleanUp();

		base.OnOpenGlDeinit(gl);
	}

	protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
	{
		try
		{
			RenderCore(gl);
		}
		catch (Exception exception)
		{
			// 渲染回调跑在合成器提交路径上, 抛出去就是进程级崩溃
			_runtime.WriteCubismLog($"桌宠渲染帧异常: {exception}");
		}
	}

	private unsafe void RenderCore(GlInterface gl)
	{
		Interlocked.Exchange(ref _framePending, 0);
		if (_glApi is null || _lapp is null) return;

		double scale = 1.0;
		if (VisualRoot is Avalonia.Controls.TopLevel topLevel)
		{
			scale = topLevel.RenderScaling;
		}

		int viewportW = (int)Math.Max(1, Math.Round(Bounds.Width * scale));
		int viewportH = (int)Math.Max(1, Math.Round(Bounds.Height * scale));
		_lastViewportW = viewportW;
		_lastViewportH = viewportH;

		gl.Viewport(0, 0, viewportW, viewportH);

		DateTime now = DateTime.UtcNow;
		float span = _lastRenderTime.Ticks == 0 ? 0.016f : (float)(now - _lastRenderTime).TotalSeconds;
		_lastRenderTime = now;

		gl.ClearColor(0, 0, 0, 0);
		gl.Clear(GlConsts.GL_COLOR_BUFFER_BIT | GlConsts.GL_DEPTH_BUFFER_BIT);

		_runtime.RenderFrame(span, viewportW, viewportH);

		// 采样全视口 alpha 缓冲供命中测试。glReadPixels 是同步点, 会强制刷新并等待 GPU,
		// 频率高了明显拖帧, 而桌宠轮廓变化很慢, 5Hz 足够。
		double nowSec = (now - DateTime.UnixEpoch).TotalSeconds;
		if (nowSec - _lastMaskSampleTime >= 0.200)
		{
			_lastMaskSampleTime = nowSec;
			SampleAlphaMask(viewportW, viewportH);
		}
	}

	private unsafe void SampleAlphaMask(int w, int h)
	{
		if (_glApi is null || w <= 0 || h <= 0) return;

		// 只回读掩码需要的那几行, 而不是整个视口:
		// glReadPixels 是同步点, 全视口 RGBA 在高 DPI 下一次就是几 MB,
		// 而掩码只需要 128 行 x 96 列个采样点。逐行回读把流量降到约 1/6,
		// 缓冲也不再随窗口尺寸变化。
		int rowBytes = w * 4;
		if (_pixelBuffer.Length != rowBytes)
		{
			_pixelBuffer = new byte[rowBytes];
		}

		// 阶段一 (无锁): 逐行回读并抽取采样点。_maskScratch 只有本方法 (渲染线程) 写,
		// 不需要持锁; 锁只保护最后的发布阶段。
		Array.Clear(_maskScratch, 0, _maskScratch.Length);

		fixed (byte* ptr = _pixelBuffer)
		{
			for (int row = 0; row < MaskHeight; row++)
			{
				// glReadPixels 自下而上返回, 掩码第 0 行 (窗口顶部) 对应缓冲的最后一行
				int sampleY = h - 1 - (int)(row * (double)h / MaskHeight);
				if (sampleY < 0 || sampleY >= h) continue;

				_glApi.GLReadPixels(0, sampleY, w, 1, _glApi.GL_RGBA, _glApi.GL_UNSIGNED_BYTE, (nint)ptr);

				for (int col = 0; col < MaskWidth; col++)
				{
					int sampleX = (int)(col * (double)w / MaskWidth);
					if (sampleX < 0 || sampleX >= w) continue;

					if (_pixelBuffer[sampleX * 4 + 3] > 16)
					{
						int maskIndex = row * MaskWidth + col;
						_maskScratch[maskIndex >> 3] |= (byte)(1 << (maskIndex & 7));
					}
				}
			}
		}

		// 阶段二 (持锁): 向外膨胀一格再作为可交互区域。掩码只用于命中测试, 不影响画面,
		// 稍微外扩能让头发、手臂这类细部件真的抓得住, 也能吃掉采样的滞后。
		lock (_maskLock)
		{
			Array.Clear(_maskBits, 0, _maskBits.Length);
			for (int row = 0; row < MaskHeight; row++)
			{
				for (int col = 0; col < MaskWidth; col++)
				{
					if (!ScratchHitNear(col, row)) continue;
					int maskIndex = row * MaskWidth + col;
					_maskBits[maskIndex >> 3] |= (byte)(1 << (maskIndex & 7));
				}
			}
		}
	}

	/// <summary>
	/// 原始掩码的 3x3 邻域内是否有命中 (膨胀用), 调用前需持有 _maskLock
	/// </summary>
	private bool ScratchHitNear(int col, int row)
	{
		for (int dr = -1; dr <= 1; dr++)
		{
			int r = row + dr;
			if (r < 0 || r >= MaskHeight) continue;
			for (int dc = -1; dc <= 1; dc++)
			{
				int c = col + dc;
				if (c < 0 || c >= MaskWidth) continue;
				int index = r * MaskWidth + c;
				if ((_maskScratch[index >> 3] & (1 << (index & 7))) != 0) return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 同步检查给定客户端坐标（DIP）是否落在模型非透明像素上
	/// </summary>
	public bool IsPointOnModel(double clientX, double clientY)
	{
		if (Bounds.Width <= 0 || Bounds.Height <= 0) return false;

		int col = (int)(clientX / Bounds.Width * MaskWidth);
		int row = (int)(clientY / Bounds.Height * MaskHeight);

		if (col < 0 || col >= MaskWidth || row < 0 || row >= MaskHeight) return false;

		bool result;
		lock (_maskLock)
		{
			int index = row * MaskWidth + col;
			result = (_maskBits[index >> 3] & (1 << (index & 7))) != 0;
		}
		return result;
	}

	private void StartRenderLoop()
	{
		if (_renderLoopRunning) return;
		_fpsCts = new CancellationTokenSource();
		_renderLoopRunning = true;
		var token = _fpsCts.Token;

		_renderThread = new Thread(() =>
		{
			while (!token.IsCancellationRequested)
			{
				int maxFps = _runtime.MaxFps;
				int targetDelayMs = maxFps > 0 ? Math.Max(1, 1000 / maxFps) : 16;

				// 上一帧还没画完就不再排队: 否则渲染慢于目标间隔时 Dispatcher 队列会无限堆积,
				// 反而把 UI 线程压死, 帧率更低而 CPU/GPU 更高
				if (Interlocked.CompareExchange(ref _framePending, 1, 0) == 0)
				{
					Dispatcher.UIThread.Post(RequestNextFrameRendering, DispatcherPriority.Render);
				}

				try
				{
					Thread.Sleep(targetDelayMs);
				}
				catch
				{
					break;
				}
			}
		})
		{
			Name = "Nori_Pet_GlRenderLoop",
			IsBackground = true,
		};
		_renderThread.Start();
	}

	private void StopRenderLoop()
	{
		if (!_renderLoopRunning) return;
		_renderLoopRunning = false;

		try
		{
			Interlocked.Exchange(ref _framePending, 0);
			_fpsCts?.Cancel();
			// 渲染线程最多还在睡一个帧间隔, 先等它退出再释放 CTS,
			// 否则线程访问已释放的 token 会抛 ObjectDisposedException
			_renderThread?.Join(200);
		}
		finally
		{
			_fpsCts?.Dispose();
			_fpsCts = null;
			_renderThread = null;
		}
	}

	/// <summary>
	/// 暂停帧驱动 (窗口隐藏时调用)
	///
	/// 窗口隐藏后合成器对帧请求的处理不可控: 要么继续全速渲染 + 采样 alpha (白烧 CPU/GPU),
	/// 要么帧请求滞留导致重新显示后停帧。显式停掉渲染线程可以同时避免这两种问题,
	/// GL 上下文保持存活, 重新显示后无缝恢复。
	/// </summary>
	public void PauseRenderLoop() => StopRenderLoop();

	/// <summary>
	/// 恢复帧驱动 (窗口重新显示时调用)
	///
	/// GL 尚未初始化时是空操作 —— 首次显示会走 OnOpenGlInit 自行启动渲染循环。
	/// </summary>
	public void ResumeRenderLoop()
	{
		if (_lapp is null || _glApi is null) return;
		// 清掉可能滞留的旧帧请求标记, 最坏情况多画一帧
		Interlocked.Exchange(ref _framePending, 0);
		StartRenderLoop();
	}
}
