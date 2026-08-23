using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Live2DCSharpSDK.App;
using Live2DCSharpSDK.Framework;
using Live2DCSharpSDK.Framework.Math;
using Live2DCSharpSDK.Framework.Motion;
using Live2DCSharpSDK.OpenGL;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Live2D;
using Nori.Core.Logging;
using Nori.Core.Resources;
using Nori.Desktop.Bridge;
using Nori.Desktop.Diagnostics;
using Nori.Desktop.Live2D.Behaviors;

namespace Nori.Desktop.Live2D;

/// <summary>
/// 原生 Live2D 桌宠运行时
///
/// 负责模型生命周期、OpenGL 渲染调度、行为管线协调以及外部命令响应。
/// </summary>
public sealed class PetRuntime
{
	private readonly AppServices _services;
	private readonly BehaviorPipeline _pipeline = new();
	private readonly ExpressionStore _expressionStore = new();
	private readonly ExpressionBehavior _expressionBehavior;
	private readonly AutoBlinkBehavior _autoBlink = new();
	private readonly EyeFocusBehavior _eyeFocus = new();
	private readonly IdleDisableBehavior _idleDisable = new();
	private readonly BeatSyncBehavior _beatSync = new();
	private readonly LipSyncBehavior _lipSync = new();
	private readonly ModelParameters _modelParams = new();
	/// <summary>行为上下文逐帧复用, 避免渲染热路径上每帧分配</summary>
	private readonly BehaviorContext _behaviorContext = new();
	private readonly Random _random = new();

	private LAppDelegateOpenGL? _app;
	private AvaloniaGlApi? _gl;
	private LAppModel? _currentModel;
	private string _currentModelId = "arg-nori";
	private string _currentModelDir = "";
	private List<MotionGroupInfo> _motionGroups = [];
	private readonly CubismMatrix44 _projectionMatrix = new();

	private double _lastUpdateTime;
	private double _lastTapTime;
	private readonly Lock _interactionGate = new();
	private PetInteractionConfig _interactionConfig = PetInteractionConfig.Empty;
	private PetViewportMapping? _viewportMapping;

	// ---- 后台模型准备 (世代归属) ----
	//
	// 模型的加载与销毁都要创建/释放 GL 纹理与着色器, 必须在 OpenGL 上下文 current 的时候做。
	// Avalonia 只在 OnOpenGlInit / OnOpenGlRender / OnOpenGlDeinit 里让上下文 current,
	// 所以磁盘扫描/JSON 解析全部放到后台 Prepare 任务; RenderFrame 每帧只观察任务,
	// 仅当"完成 + 未取消 + 世代仍匹配"时才在 GL 区做最小资源交换。
	private readonly Lock _prepareGate = new();
	private long _modelGeneration;
	private CancellationTokenSource? _prepareCts;
	private Task<PreparedModel?>? _prepareTask;

	// 配置项
	public float UserScale { get; set; } = 1.0f;
	public float Opacity { get; set; } = 1.0f;
	public bool AutoBlinkEnabled { get; set; } = true;
	public bool EyeTrackingEnabled { get; set; } = true;
	public bool IdleEyeAnimationEnabled { get; set; } = true;
	public bool IdleAnimationEnabled { get; set; } = true;
	public bool ExpressionEnabled { get; set; } = true;
	public bool ShadowEnabled { get; set; } = true;
	public bool LipSyncEnabled { get; set; } = true;
	public bool BeatSyncEnabled { get; set; }
	public bool ClickInteraction { get; set; } = true;
	public int MaxFps { get; set; }

	public event Action? ModelChanged;
	public event Action? ModelLoadRequested;
	public event Action? FrameRendered;
	public event Action<PetInteractionTrigger>? InteractionTriggered;

	/// <summary>当前模型加载世代, AI 结果应用前用它判断是否已经切换模型。</summary>
	public long ModelGeneration => Volatile.Read(ref _modelGeneration);

	/// <summary>当前模型的自定义互动配置快照。</summary>
	public PetInteractionConfig InteractionConfig
	{
		get
		{
			lock (_interactionGate) return _interactionConfig;
		}
	}

	/// <summary>缩放变化: 桌宠窗口据此重算窗口尺寸</summary>
	public event Action? LayoutChanged;

	public PetRuntime(AppServices services)
	{
		_services = services;
		_expressionBehavior = new ExpressionBehavior(_expressionStore);

		// 注册行为插件（pre / post / final）
		_pipeline.Register(_idleDisable, PipelineStage.Pre);
		_pipeline.Register(_beatSync, PipelineStage.Pre);
		_pipeline.Register(_eyeFocus, PipelineStage.Post);
		_pipeline.Register(_expressionBehavior, PipelineStage.Final);
		_pipeline.Register(_autoBlink, PipelineStage.Final);
		_pipeline.Register(_lipSync, PipelineStage.Final);
	}

	public LAppModel? CurrentModel => _currentModel;
	public string CurrentModelId => _currentModelId;
	public IReadOnlyList<MotionGroupInfo> MotionGroups => _motionGroups;
	public IReadOnlyList<string> Expressions => _expressionStore.AllGroupNames().Count > 0
		? _expressionStore.AllGroupNames()
		: _expressionStore.AllNames();

	/// <summary>
	/// Cubism SDK 日志落到应用自己的文件日志
	///
	/// 绝不能用 Console.WriteLine: 宿主是 WinExe 没有控制台, 写控制台会抛 IOException,
	/// 而 Cubism 的日志是在渲染回调里发出的, 抛出去就是进程崩溃。
	/// </summary>
	public void WriteCubismLog(string message)
	{
		try
		{
			_services.Logger.Write(LogSource.Backend, "warn", $"[Live2D] {message}");
		}
		catch
		{
			// 日志本身失败时保持静默, 绝不允许把异常带回渲染线程
		}
	}

	public void OnGlInit(LAppDelegateOpenGL app, AvaloniaGlApi gl)
	{
		_app = app;
		_gl = gl;
		_services.Logger.Write(LogSource.Backend, "info", "Live2D OpenGL 初始化完成");
		string savedModel = _services.Config.GetStringOr("selected_model", "arg-nori");
		if (!string.IsNullOrWhiteSpace(savedModel)) _currentModelId = savedModel.Trim();
		LoadConfigs();
		RequestModelLoad(_currentModelId);
	}

	public void OnGlDeinit()
	{
		lock (_prepareGate)
		{
			_prepareCts?.Cancel();
			_prepareCts?.Dispose();
			_prepareCts = null;
			_prepareTask = null;
		}
		// 同上: 释放交给 manager, PetGlControl 随后的 _lapp.Dispose() 会走到 ReleaseAllModel()
		_currentModel = null;
		lock (_interactionGate) _viewportMapping = null;
		_app?.Live2dManager.ReleaseAllModel();
		_app = null;
		_gl = null;
	}

	public void LoadConfigs()
	{
		UserScale = ParseFloatConfig($"l2d_scale_{_currentModelId}", ParseFloatConfig("l2d_scale", 1.0f));
		Opacity = ParseFloatConfig("l2d_opacity", 1.0f);
		AutoBlinkEnabled = ParseBoolConfig("l2d_auto_blink", true);
		EyeTrackingEnabled = ParseBoolConfig("l2d_eye_tracking", true);
		IdleEyeAnimationEnabled = ParseBoolConfig("l2d_idle_eye_animation", true);
		IdleAnimationEnabled = ParseBoolConfig("l2d_idle_animation", true);
		ExpressionEnabled = ParseBoolConfig("l2d_expression_enabled", true);
		ShadowEnabled = ParseBoolConfig("l2d_shadow", true);
		LipSyncEnabled = ParseBoolConfig("l2d_lip_sync", true);
		BeatSyncEnabled = ParseBoolConfig("l2d_beat_sync", false);
		ClickInteraction = ParseBoolConfig("l2d_click_interaction", true);
		MaxFps = (int)ParseFloatConfig("l2d_max_fps", 0.0f);
		LoadInteractionConfig();
	}

	/// <summary>读取当前模型的自定义互动配置；损坏配置按空配置处理。</summary>
	private void LoadInteractionConfig()
	{
		PetInteractionConfig config = PetInteractionConfig.Empty;
		if (_services.Config.Get(PetInteractionConfig.StorageKey(_currentModelId)) is ConfigValue.Json {Value: JsonNode node})
		{
			try
			{
				config = PetInteractionConfig.Parse(node.ToJsonString(PetInteractionJson.Options));
			}
			catch (Exception exception)
			{
				WriteCubismLog($"互动配置无效 [{_currentModelId}]: {exception.Message}");
			}
		}
		lock (_interactionGate) _interactionConfig = config;
	}

	/// <summary>桥接保存配置后的当前模型热应用。</summary>
	public void SetInteractionConfig(string modelId, PetInteractionConfig config)
	{
		if (!modelId.Equals(_currentModelId, StringComparison.OrdinalIgnoreCase)) return;
		config.Validate();
		lock (_interactionGate) _interactionConfig = config;
	}

	/// <summary>
	/// 读数值配置
	///
	/// ConfigValue 是 record, 直接 ToString() 会得到 "Integer { Value = 30 }" 这种调试字符串,
	/// 必须走 AsStringOr. 另外 ConfigStore 读取时会重新推断类型, 存进去的 "1" / "0" 会变成
	/// 布尔再还原成 "true" / "false", 所以这里要一并接住 (与前端 parseNumber 的处境相同).
	/// </summary>
	private float ParseFloatConfig(string key, float fallback)
	{
		string raw = _services.Config.GetStringOr(key, "");
		if (raw.Length == 0) return fallback;
		if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return 1.0f;
		if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return 0.0f;
		return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
	}

	/// <summary>
	/// 读布尔配置 (同样要走 AsStringOr, 并接住 "1" / "0")
	/// </summary>
	private bool ParseBoolConfig(string key, bool fallback)
	{
		string raw = _services.Config.GetStringOr(key, "");
		return ParseBool(raw) ?? fallback;
	}

	/// <summary>
	/// 解析布尔文本: 与前端 parseBoolean 同口径
	/// </summary>
	private static bool? ParseBool(string raw) => raw switch
	{
		"1" => true,
		"0" => false,
		_ when raw.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
		_ when raw.Equals("false", StringComparison.OrdinalIgnoreCase) => false,
		_ => null,
	};

	/// <summary>
	/// 解析数值文本: 与 ParseFloatConfig 同口径, 供配置热更新使用
	/// </summary>
	private static float? ParseFloat(string raw)
	{
		if (raw.Equals("true", StringComparison.OrdinalIgnoreCase)) return 1.0f;
		if (raw.Equals("false", StringComparison.OrdinalIgnoreCase)) return 0.0f;
		return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : null;
	}

	/// <summary>
	/// 请求切换模型 (线程安全): 递增世代并在后台开始元数据准备.
	///
	/// 上一份准备任务的 CTS 立即取消; 渲染帧只消费完成且世代匹配的结果。
	/// </summary>
	public void RequestModelLoad(string modelId)
	{
		if (string.IsNullOrWhiteSpace(modelId)) return;
		string trimmed = modelId.Trim();

		lock (_prepareGate)
		{
			_modelGeneration++;
			long generation = _modelGeneration;
			_prepareCts?.Cancel();
			_prepareCts?.Dispose();
			CancellationTokenSource cts = new();
			_prepareCts = cts;

			string modelDir = _services.Resources.ResourceDir(ResourceType.Live2D, trimmed);
			_prepareTask = Task.Run(async () =>
			{
				try
				{
					return await ModelPreparation.PrepareAsync(trimmed, modelDir, generation, cts.Token);
				}
				catch (OperationCanceledException)
				{
					return null;
				}
				catch (Exception exception)
				{
					// 准备失败不带入渲染线程, 保留当前工作模型继续渲染
					try
					{
						_services.Logger.Write(LogSource.Backend, "error", $"后台准备 Live2D 模型失败 [{trimmed}]: {exception.Message}");
					}
					catch
					{
						// 日志失败保持静默
					}
					return null;
				}
			}, CancellationToken.None);
		}
		try { ModelLoadRequested?.Invoke(); }
		catch (Exception exception) { WriteCubismLog($"模型切换取消互动请求失败: {exception.Message}"); }
	}

	/// <summary>
	/// 在渲染帧观察准备任务; 只有已完成且世代仍匹配时才在 GL 区消费结果
	/// </summary>
	private void ConsumePreparedIfReady()
	{
		Task<PreparedModel?>? task;
		long generation;
		lock (_prepareGate)
		{
			task = _prepareTask;
			generation = _modelGeneration;
			if (task is not { IsCompletedSuccessfully: true }) return;
			_prepareTask = null;
		}

		PreparedModel? prepared;
		try
		{
			prepared = task.Result;
		}
		catch (Exception exception)
		{
			_services.Logger.Write(LogSource.Backend, "warn", $"读取 Live2D 准备结果失败: {exception.Message}");
			return;
		}

		if (prepared is null || prepared.Generation != generation) return;
		ApplyPreparedOnGlThread(prepared);
	}

	/// <summary>
	/// GL 线程专属: 用已准备的元数据执行 SDK 资源交换、renderer 设置与表情提交
	/// </summary>
	private void ApplyPreparedOnGlThread(PreparedModel prepared)
	{
		if (_app is null || _gl is null || !Directory.Exists(prepared.ModelDir)) return;

		bool firstLoadOfModel = !string.Equals(prepared.ModelId, _currentModelId, StringComparison.Ordinal);

		try
		{
			// 模型归 LAppLive2DManager 所有, 只能由它释放。
			// 这里如果先 _currentModel.Dispose() 再 ReleaseAllModel(), 同一个 CubismMoc 会被
			// DeallocateAligned 两次, 直接堆损坏 (0xC0000374)。
			_currentModel = null;
			_app.Live2dManager.ReleaseAllModel();

			_currentModel = _app.Live2dManager.LoadModel(prepared.ModelDir, prepared.Model3FileName);
			_currentModelId = prepared.ModelId;
			_currentModelDir = prepared.ModelDir;
			lock (_interactionGate) _viewportMapping = null;

			// 自定义值更新挂钩
			_currentModel.CustomValueUpdate = true;
			_currentModel.ValueUpdate = OnModelValueUpdate;

			// 高画质渲染配置
			//
			// UseHighPrecisionMask 必须保持关闭: 打开后 SDK 会对每一个被蒙版裁剪的部件
			// 单独把整张蒙版缓冲清空并重画一遍 (CubismRenderer_OpenGLES2.DoDrawModel),
			// 2048x2048 x 几十个部件 x 60fps 的填充率足以把 GPU 打满。
			// 关闭后所有蒙版每帧只渲染一次, 画质由缓冲尺寸保证即可。
			if (_currentModel.Renderer is CubismRenderer_OpenGLES2 renderer)
			{
				renderer.SetClippingMaskBufferSize(2048, 2048);
				renderer.Anisotropy = 16.0f;
				renderer.UseHighPrecisionMask = false;
			}

			// 提交预解析的动作组与表情定义 (同步, 无 I/O)
			_motionGroups = [.. prepared.MotionGroups];
			_expressionBehavior.ApplyPrepared(prepared, _currentModel.Model);

			// 显示参数随模型首次载入重新读取配置
			if (firstLoadOfModel) LoadConfigs();

			_services.Logger.Write(LogSource.Backend, "info", $"成功加载 Live2D 模型: {prepared.ModelId}");
			ModelChanged?.Invoke();
		}
		catch (Exception exception)
		{
			// 加载失败时不清空工作现场之外的状态; 下一帧继续用旧数据渲染
			_services.Logger.Write(LogSource.Backend, "error", $"加载 Live2D 模型失败 [{prepared.ModelId}]: {exception.Message}");
		}
	}

	private void OnModelValueUpdate(LAppModel model)
	{
		double now = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
		double timeDelta = _lastUpdateTime > 0 ? now - _lastUpdateTime : 0.016;
		_lastUpdateTime = now;

		// "空闲" = 没有动作在播, 或播的是待机组。LAppModel.Update() 会在动作播完时
		// 自动补一个随机 Idle, 所以只判 IsMotionFinished() 会让 isIdleMotion 几乎永远为 false,
		// 自动眨眼 / 空闲眼神微动 / idle-disable 三个行为都不会触发。
		bool isIdleMotion = model.IsMotionFinished()
			|| string.Equals(model.CurrentMotionGroup, LAppDefine.MotionGroupIdle, StringComparison.OrdinalIgnoreCase);

		var ctx = _behaviorContext;
		ctx.ResetFrame();
		ctx.Model = model;
		ctx.Now = now;
		ctx.TimeDelta = timeDelta;
		ctx.IsIdleMotion = isIdleMotion;
		ctx.AutoBlinkEnabled = AutoBlinkEnabled;
		ctx.EyeTrackingEnabled = EyeTrackingEnabled;
		ctx.IdleEyeAnimationEnabled = IdleEyeAnimationEnabled;
		ctx.IdleAnimationEnabled = IdleAnimationEnabled;
		ctx.ForceIdleEyeAnimation = IdleEyeAnimationEnabled;
		ctx.BeatSyncEnabled = BeatSyncEnabled;
		ctx.LipSyncEnabled = LipSyncEnabled;
		ctx.ExpressionEnabled = ExpressionEnabled;
		ctx.ClickInteraction = ClickInteraction;
		ctx.ModelParameters = _modelParams;

		// 运行 pre 插件（如 IdleDisable、BeatSync）
		_pipeline.RunPre(ctx);

		// 如果未短路且开启了眼部追踪，补回 SDK 拖拽角度
		if (EyeTrackingEnabled)
		{
			float dragX = model.DragX;
			float dragY = model.DragY;
			model.Model.AddParameterValue(model.IdParamAngleX, dragX * 30);
			model.Model.AddParameterValue(model.IdParamAngleY, dragY * 30);
			model.Model.AddParameterValue(model.IdParamAngleZ, dragX * dragY * -30);
			model.Model.AddParameterValue(model.IdParamBodyAngleX, dragX * 10);
			model.Model.AddParameterValue(model.IdParamEyeBallX, dragX);
			model.Model.AddParameterValue(model.IdParamEyeBallY, dragY);
		}

		// 运行 post 插件（如 EyeFocus 眼神微动）
		_pipeline.RunPost(ctx);

		// 运行 final 插件（如 Expression、AutoBlink、LipSync）
		_pipeline.RunFinal(ctx);
	}

	public void RenderFrame(float deltaTime, int viewportWidth, int viewportHeight)
	{
		if (_gl is null) return;

		// 此刻 OpenGL 上下文才是 current 的: 仅消费完成且世代匹配的准备结果,
		// 未完成或失败时继续渲染现有模型
		ConsumePreparedIfReady();

		if (_currentModel is null) return;

		// LAppModel.Update() 读的是 LAppPal.DeltaTime。这里没有走 LAppDelegate.Run(),
		// 不显式写入的话它永远是 0, 动作队列 / 物理 / 呼吸会全部冻结在首帧。
		LAppPal.DeltaTime = deltaTime;

		// 投影: 先做纵横比校正, 再按需等比缩小到窗口内
		//
		// 模型没有 Layout 时 CubismModelMatrix 的构造函数会 SetHeight(2.0), 也就是把模型高度
		// 规范化成整个 NDC 高度, 宽度则是 2 x 模型宽高比。NDC 的 x 覆盖 viewportWidth、
		// y 覆盖 viewportHeight, 所以校正量只跟窗口有关: scaleX = viewportHeight / viewportWidth。
		// 之前这里乘的是 aspectModel / aspectWindow, 等于多乘了一个模型宽高比,
		// 竖长模型 (宽高比 < 1) 会被按这个比例横向压扁。
		_projectionMatrix.LoadIdentity();
		float canvasPixelW = _currentModel.Model.GetCanvasWidthPixel();
		float canvasPixelH = _currentModel.Model.GetCanvasHeightPixel();
		float canvasUnitW = _currentModel.Model.GetCanvasWidth();
		float canvasUnitH = _currentModel.Model.GetCanvasHeight();

		float aspectWindow = (float)viewportWidth / viewportHeight;
		float aspectModel = canvasPixelW > 0 && canvasPixelH > 0 ? canvasPixelW / canvasPixelH : 1.0f;

		float scaleX = (float)viewportHeight / viewportWidth;
		float scaleY = 1.0f;

		// 此时模型正好占满窗口高度; 只有模型比窗口更宽时才需要整体缩小,
		// 保证任何窗口比例下模型都完整可见 (安全基准尺寸的上下限收口会让两者略有出入)
		if (aspectModel > aspectWindow)
		{
			float fit = aspectWindow / aspectModel;
			scaleX *= fit;
			scaleY *= fit;
		}

		_projectionMatrix.Scale(scaleX, scaleY);
		PetViewportMapping mapping = PetViewportMapping.Create(
			viewportWidth,
			viewportHeight,
			// ModelMatrix 由 Cubism 的 Unit 画布尺寸构造；这里必须使用同一单位，
			// 不能传 Pixel 尺寸，否则 PixelsPerUnit 会把归一化点击压缩到画布中心。
			canvasUnitW,
			canvasUnitH,
			_currentModel.ModelMatrix.GetScaleX(),
			_currentModel.ModelMatrix.GetScaleY(),
			_currentModel.ModelMatrix.GetTranslateX(),
			_currentModel.ModelMatrix.GetTranslateY());
		lock (_interactionGate) _viewportMapping = mapping;

		_currentModel.Update();
		_currentModel.Draw(_projectionMatrix);
		FrameRendered?.Invoke();
	}

	public void LookAt(float clientX, float clientY, float windowW, float windowH)
	{
		if (_currentModel is null || !EyeTrackingEnabled || windowW <= 0 || windowH <= 0) return;
		float normX = Math.Clamp((clientX / windowW) * 2.0f - 1.0f, -1.0f, 1.0f);
		float normY = Math.Clamp(-((clientY / windowH) * 2.0f - 1.0f), -1.0f, 1.0f);
		_currentModel.SetDragging(normX, normY);
	}

	public void HandleTap(float clientX, float clientY, float windowW, float windowH)
	{
		if (_currentModel is null || !ClickInteraction || windowW <= 0 || windowH <= 0) return;

		double now = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
		if (now - _lastTapTime < 0.6) return;
		_lastTapTime = now;

		PetViewportMapping? mapping;
		PetInteractionConfig interactions;
		lock (_interactionGate)
		{
			mapping = _viewportMapping;
			interactions = _interactionConfig;
		}

		if (mapping is { } currentMapping
			&& currentMapping.TryMapClientToModel(clientX, clientY, out double modelX, out double modelY)
			&& PetInteractionResolver.TryResolve(interactions, modelX, modelY, out PetInteractionHit? hit)
			&& hit is not null)
		{
			if (hit.Region.ReactionMode == PetInteractionReactionMode.Ai)
			{
				PetInteractionTrigger trigger = new(_currentModelId, ModelGeneration, hit);
				Action<PetInteractionTrigger>? handler = InteractionTriggered;
				if (handler is null)
				{
					ApplyLocalInteraction(hit.Region);
				}
				else
				{
					try { handler(trigger); }
					catch (Exception exception)
					{
						WriteCubismLog($"AI 互动调度失败: {exception.Message}");
						ApplyLocalInteraction(hit.Region);
					}
				}
			}
			else
			{
				ApplyLocalInteraction(hit.Region);
			}
			return;
		}

		// 没有 HitAreas 的模型也会走这里: PetWindow 已经用 alpha 掩码确认点击的是模型像素。
		float normX = (clientX / windowW) * 2.0f - 1.0f;
		float normY = -((clientY / windowH) * 2.0f - 1.0f);
		if (_currentModel.HitTest(LAppDefine.HitAreaNameHead, normX, normY)
			&& ExpressionEnabled
			&& ToggleRandomExpression())
		{
			return;
		}
		PlayTapBodyOrRandomMotion();
	}

	/// <summary>执行一个区域配置的本地 Motion/Expression 反应。</summary>
	public void ApplyLocalInteraction(PetInteractionRegion region)
	{
		switch (region.Motion.Mode)
		{
			case PetInteractionActionMode.Random:
				PlayRandomMotion();
				break;
			case PetInteractionActionMode.Selected:
				PlayMotionExact(region.Motion.Group ?? "", region.Motion.Name ?? "");
				break;
		}

		if (!ExpressionEnabled) return;
		switch (region.Expression.Mode)
		{
			case PetInteractionActionMode.Random:
				ToggleRandomExpression();
				break;
			case PetInteractionActionMode.Selected:
				PlayExpression(region.Expression.Name ?? "");
				break;
		}
	}

	private bool PlayMotionExact(string group, string name)
	{
		MotionGroupInfo? matched = FindMotionGroup(group);
		if (matched is null) return false;
		int index = matched.Names.FindIndex(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
		return index >= 0 && TryStartMotion(matched.Group, index) is not null;
	}

	/// <summary>
	/// 播放点击互动动作。动作组按语义优先级选择，同组从随机位置开始逐项尝试。
	/// </summary>
	public bool PlayTapBodyOrRandomMotion()
	{
		foreach (MotionGroupInfo group in MotionSelector.GetInteractionCandidates(_motionGroups))
		{
			if (TryPlayGroup(group)) return true;
		}
		return false;
	}

	private bool TryPlayGroup(MotionGroupInfo group)
	{
		if (_currentModel is null || group.Names.Count == 0) return false;

		int start = _random.Next(group.Names.Count);
		for (int offset = 0; offset < group.Names.Count; offset++)
		{
			int index = (start + offset) % group.Names.Count;
			if (TryStartMotion(group.Group, index) is not null) return true;
		}
		return false;
	}

	private CubismMotionQueueEntry? TryStartMotion(string group, int index)
	{
		if (_currentModel is null) return null;
		try
		{
			return _currentModel.StartMotion(group, index, MotionPriority.PriorityForce);
		}
		catch (Exception ex)
		{
			WriteCubismLog($"动作播放异常 [{group}_{index}]: {ex.Message}");
			return null;
		}
	}

	private MotionGroupInfo? FindMotionGroup(string group)
	{
		if (string.IsNullOrWhiteSpace(group)) return null;
		return _motionGroups.FirstOrDefault(item => item.Group.Equals(group, StringComparison.OrdinalIgnoreCase));
	}

	public bool PlayMotionByName(string name)
	{
		if (_currentModel is null || string.IsNullOrWhiteSpace(name)) return false;
		string? resolved = PetActionResolver.ResolveMotion(_motionGroups, name);
		if (resolved is null) return false;
		foreach (MotionGroupInfo group in _motionGroups)
		{
			int index = group.Names.FindIndex(item => item.Equals(resolved, StringComparison.OrdinalIgnoreCase));
			if (index >= 0) return TryStartMotion(group.Group, index) is not null;
		}
		return false;
	}

	public bool PlayMotionByIndex(string group, int no)
	{
		MotionGroupInfo? matched = FindMotionGroup(group);
		if (matched is null || no < 0 || no >= matched.Names.Count) return false;
		return TryStartMotion(matched.Group, no) is not null;
	}

	public bool PlayRandomMotion()
	{
		IReadOnlyList<MotionGroupInfo> candidates = MotionSelector.GetInteractionCandidates(_motionGroups);
		if (candidates.Count == 0) return false;

		int start = _random.Next(candidates.Count);
		for (int offset = 0; offset < candidates.Count; offset++)
		{
			MotionGroupInfo group = candidates[(start + offset) % candidates.Count];
			if (TryPlayGroup(group)) return true;
		}
		return false;
	}

	public bool PlayExpression(string name)
	{
		string? resolved = PetActionResolver.ResolveExpression(Expressions, name);
		return resolved is not null && _expressionStore.Play(resolved);
	}
	public void StopExpression() => _expressionStore.Stop();
	public void ToggleExpression(string name) => _expressionStore.Toggle(name);

	public bool ToggleRandomExpression()
	{
		IReadOnlyList<string> names = _expressionStore.AllGroupNames();
		if (names.Count == 0) names = _expressionStore.AllNames();
		if (names.Count == 0) return false;

		string randomName = names[_random.Next(names.Count)];
		return _expressionStore.Toggle(randomName);
	}

	public void SetMouthOpen(float value, bool speaking)
	{
		_lipSync.SetMouthOpen(value);
		_lipSync.SetNowSpeaking(speaking);
	}

	public void TriggerBeat(double? timestamp = null)
	{
		if (BeatSyncEnabled) _beatSync.TriggerBeat(timestamp);
	}

	/// <summary>
	/// 配置删除后的运行时复位: 让桌宠回到该 key 的内置默认值, 与 set/delete 的状态转换对称
	/// </summary>
	public void ApplyConfigDelete(string key)
	{
		switch (key)
		{
			case "selected_model":
				if (ConfigStore.DefaultModel != _currentModelId) RequestModelLoad(ConfigStore.DefaultModel);
				break;
			case "l2d_opacity":
				Opacity = 1.0f;
				break;
			case "l2d_max_fps":
				MaxFps = 0;
				break;
			case "l2d_auto_blink": AutoBlinkEnabled = true; break;
			case "l2d_eye_tracking": EyeTrackingEnabled = true; break;
			case "l2d_idle_eye_animation": IdleEyeAnimationEnabled = true; break;
			case "l2d_idle_animation": IdleAnimationEnabled = true; break;
			case "l2d_expression_enabled": ExpressionEnabled = true; break;
			case "l2d_shadow": ShadowEnabled = true; break;
			case "l2d_lip_sync": LipSyncEnabled = true; break;
			case "l2d_beat_sync": BeatSyncEnabled = false; break;
			case "l2d_click_interaction": ClickInteraction = true; break;
			default: break;
		}

		// 缩放按模型存储 (l2d_scale_<modelId>), 兼容旧的全局键
		if (key == $"l2d_scale_{_currentModelId}" || key == "l2d_scale")
		{
			UserScale = 1.0f;
			LayoutChanged?.Invoke();
		}
	}

	/// <summary>
	/// 配置热更新
	///
	/// value 是 ConfigValue.ToStorage() 的结果: 布尔存成 "1" / "0", 所以必须走 ParseBool,
	/// 直接 bool.TryParse 会让所有开关的热更新静默失效.
	/// </summary>
	public void ApplyConfig(string key, string value)
	{
		if (key == "selected_model" && !string.IsNullOrWhiteSpace(value))
		{
			if (value.Trim() != _currentModelId) RequestModelLoad(value);
			return;
		}

		// 缩放按模型存储 (l2d_scale_<modelId>), 兼容旧的全局键
		if (key == $"l2d_scale_{_currentModelId}" || key == "l2d_scale")
		{
			if (ParseFloat(value) is { } scale)
			{
				UserScale = Math.Clamp(scale, 0.1f, 2.0f);
				LayoutChanged?.Invoke();
			}
			return;
		}

		if (key == "l2d_opacity")
		{
			if (ParseFloat(value) is { } opacity) Opacity = Math.Clamp(opacity, 0.0f, 1.0f);
			return;
		}

		switch (key)
		{
			case "l2d_auto_blink" when ParseBool(value) is { } v: AutoBlinkEnabled = v; break;
			case "l2d_eye_tracking" when ParseBool(value) is { } v: EyeTrackingEnabled = v; break;
			case "l2d_idle_eye_animation" when ParseBool(value) is { } v: IdleEyeAnimationEnabled = v; break;
			case "l2d_idle_animation" when ParseBool(value) is { } v: IdleAnimationEnabled = v; break;
			case "l2d_expression_enabled" when ParseBool(value) is { } v: ExpressionEnabled = v; break;
			case "l2d_shadow" when ParseBool(value) is { } v: ShadowEnabled = v; break;
			case "l2d_lip_sync" when ParseBool(value) is { } v: LipSyncEnabled = v; break;
			case "l2d_beat_sync" when ParseBool(value) is { } v: BeatSyncEnabled = v; break;
			case "l2d_click_interaction" when ParseBool(value) is { } v: ClickInteraction = v; break;
			case "l2d_max_fps" when ParseFloat(value) is { } v: MaxFps = (int)v; break;
			default: break;
		}
	}
}
