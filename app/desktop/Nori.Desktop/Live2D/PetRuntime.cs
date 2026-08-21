using System.Globalization;
using System.Text.Json;
using Avalonia.Threading;
using Live2DCSharpSDK.App;
using Live2DCSharpSDK.Framework;
using Live2DCSharpSDK.Framework.Math;
using Live2DCSharpSDK.Framework.Motion;
using Live2DCSharpSDK.OpenGL;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Logging;
using Nori.Core.Resources;
using Nori.Desktop.Bridge;
using Nori.Desktop.Live2D.Behaviors;

namespace Nori.Desktop.Live2D;

public sealed record MotionGroupInfo
{
	public required string Group { get; init; }
	public required List<string> Names { get; init; }
}

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

	/// <summary>
	/// 待切换的模型 id
	///
	/// 模型的加载与销毁都要创建/释放 GL 纹理与着色器, 必须在 OpenGL 上下文 current 的时候做。
	/// Avalonia 只在 OnOpenGlInit / OnOpenGlRender / OnOpenGlDeinit 里让上下文 current,
	/// 直接从桥接线程或 Dispatcher 上调 LoadModel 会拿不到上下文并崩掉渲染线程,
	/// 所以这里只登记请求, 真正的切换发生在下一帧的 RenderFrame 开头。
	/// </summary>
	private string? _pendingModelId;

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
	public event Action? FrameRendered;

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
		LoadInitialModel();
	}

	public void OnGlDeinit()
	{
		// 同上: 释放交给 manager, PetGlControl 随后的 _lapp.Dispose() 会走到 ReleaseAllModel()
		_currentModel = null;
		_app?.Live2dManager.ReleaseAllModel();
		_app = null;
		_gl = null;
	}

	private void LoadInitialModel()
	{
		string savedModel = _services.Config.GetStringOr("selected_model", "arg-nori");
		if (!string.IsNullOrWhiteSpace(savedModel)) _currentModelId = savedModel.Trim();
		LoadConfigs();
		LoadModel(_currentModelId);
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
	/// 请求切换模型 (线程安全), 真正的加载在下一帧的 GL 回调里执行
	/// </summary>
	public void RequestModelLoad(string modelId)
	{
		if (string.IsNullOrWhiteSpace(modelId)) return;
		Interlocked.Exchange(ref _pendingModelId, modelId.Trim());
	}

	public bool LoadModel(string modelId)
	{
		if (_app is null || _gl is null) return false;

		string modelDir = _services.Resources.ResourceDir(ResourceType.Live2D, modelId);
		if (!Directory.Exists(modelDir))
		{
			_services.Logger.Write(LogSource.Backend, "warn", $"Live2D 模型目录不存在: {modelDir}");
			return false;
		}

		string[] model3Files = Directory.GetFiles(modelDir, "*.model3.json", SearchOption.TopDirectoryOnly);
		if (model3Files.Length == 0)
		{
			_services.Logger.Write(LogSource.Backend, "warn", $"目录中未找到 *.model3.json: {modelDir}");
			return false;
		}

		try
		{
			// 模型归 LAppLive2DManager 所有, 只能由它释放。
			// 这里如果先 _currentModel.Dispose() 再 ReleaseAllModel(), 同一个 CubismMoc 会被
			// DeallocateAligned 两次, 直接堆损坏 (0xC0000374)。
			_currentModel = null;
			_app.Live2dManager.ReleaseAllModel();

			string modelJsonName = Path.GetFileName(model3Files[0]);
			_currentModel = _app.Live2dManager.LoadModel(modelDir, modelJsonName);
			_currentModelId = modelId;
			_currentModelDir = modelDir;

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

			// 解析动作组
			ExtractMotionGroups(model3Files[0]);

			// 解析表情文件
			ExtractExpressions(model3Files[0]);

			_services.Logger.Write(LogSource.Backend, "info", $"成功加载 Live2D 模型: {modelId}");
			ModelChanged?.Invoke();
			return true;
		}
		catch (Exception ex)
		{
			_services.Logger.Write(LogSource.Backend, "error", $"加载 Live2D 模型失败 [{modelId}]: {ex}");
			return false;
		}
	}

	private void ExtractMotionGroups(string model3JsonPath)
	{
		_motionGroups = [];
		try
		{
			string json = File.ReadAllText(model3JsonPath);
			using var doc = JsonDocument.Parse(json);
			if (doc.RootElement.TryGetProperty("FileReferences", out var fileRefs) &&
			    fileRefs.TryGetProperty("Motions", out var motions))
			{
				foreach (var groupProp in motions.EnumerateObject())
				{
					List<string> names = [];
					foreach (var item in groupProp.Value.EnumerateArray())
					{
						if (item.TryGetProperty("File", out var fileProp))
						{
							string file = fileProp.GetString() ?? "";
							string name = Path.GetFileNameWithoutExtension(file).Replace(".motion3", "");
							if (!string.IsNullOrEmpty(name)) names.Add(name);
						}
					}
					if (names.Count > 0)
					{
						_motionGroups.Add(new MotionGroupInfo { Group = groupProp.Name, Names = names });
					}
				}
			}
		}
		catch (Exception ex)
		{
			_services.Logger.Write(LogSource.Backend, "warn", $"解析动作组异常: {ex.Message}");
		}
	}

	private void ExtractExpressions(string model3JsonPath)
	{
		List<(string Name, string File)> expRefs = [];
		try
		{
			string json = File.ReadAllText(model3JsonPath);
			using var doc = JsonDocument.Parse(json);
			if (doc.RootElement.TryGetProperty("FileReferences", out var fileRefs) &&
			    fileRefs.TryGetProperty("Expressions", out var expressions))
			{
				foreach (var item in expressions.EnumerateArray())
				{
					string name = item.GetProperty("Name").GetString() ?? "";
					string file = item.GetProperty("File").GetString() ?? "";
					if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(file))
					{
						expRefs.Add((name, file));
					}
				}
			}

			if (_currentModel != null)
			{
				_ = _expressionBehavior.InitializeAsync(_currentModelDir, expRefs, _currentModel.Model);
			}
		}
		catch (Exception ex)
		{
			_services.Logger.Write(LogSource.Backend, "warn", $"解析表情异常: {ex.Message}");
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

		// 模型切换在这里落地: 此刻 OpenGL 上下文才是 current 的
		if (Interlocked.Exchange(ref _pendingModelId, null) is { } pending)
		{
			_currentModelId = pending;
			LoadConfigs();
			LoadModel(pending);
		}

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
		float canvasW = _currentModel.Model.GetCanvasWidthPixel();
		float canvasH = _currentModel.Model.GetCanvasHeightPixel();

		float aspectWindow = (float)viewportWidth / viewportHeight;
		float aspectModel = canvasW > 0 && canvasH > 0 ? canvasW / canvasH : 1.0f;

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

		float normX = (clientX / windowW) * 2.0f - 1.0f;
		float normY = -((clientY / windowH) * 2.0f - 1.0f);

		if (_currentModel.HitTest(LAppDefine.HitAreaNameHead, normX, normY))
		{
			ToggleRandomExpression();
			return;
		}

		PlayTapBodyOrRandomMotion();
	}

	public void PlayTapBodyOrRandomMotion()
	{
		if (_currentModel is null) return;

		var entry = _currentModel.StartMotion(LAppDefine.MotionGroupTapBody, 0, MotionPriority.PriorityForce);
		if (entry is null)
		{
			string[] candidateGroups = ["Idle", "Reactions", "Poses", "Effects"];
			foreach (string group in candidateGroups)
			{
				var matched = _motionGroups.FirstOrDefault(g => g.Group.Equals(group, StringComparison.OrdinalIgnoreCase));
				if (matched != null && matched.Names.Count > 0)
				{
					int idx = _random.Next(matched.Names.Count);
					_currentModel.StartMotion(matched.Group, idx, MotionPriority.PriorityForce);
					break;
				}
			}
		}
	}

	public bool PlayMotionByName(string name)
	{
		if (_currentModel is null) return false;
		foreach (var group in _motionGroups)
		{
			int idx = group.Names.FindIndex(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
			if (idx >= 0)
			{
				var entry = _currentModel.StartMotion(group.Group, idx, MotionPriority.PriorityForce);
				return entry != null;
			}
		}
		return false;
	}

	public bool PlayMotionByIndex(string group, int no)
	{
		if (_currentModel is null) return false;
		var entry = _currentModel.StartMotion(group, no, MotionPriority.PriorityForce);
		return entry != null;
	}

	public void PlayRandomMotion()
	{
		if (_currentModel is null || _motionGroups.Count == 0) return;
		var group = _motionGroups[_random.Next(_motionGroups.Count)];
		if (group.Names.Count > 0)
		{
			int idx = _random.Next(group.Names.Count);
			_currentModel.StartMotion(group.Group, idx, MotionPriority.PriorityForce);
		}
	}

	public void PlayExpression(string name) => _expressionStore.Play(name);
	public void StopExpression() => _expressionStore.Stop();
	public void ToggleExpression(string name) => _expressionStore.Toggle(name);

	public void ToggleRandomExpression()
	{
		var groupNames = _expressionStore.AllGroupNames();
		if (groupNames.Count > 0)
		{
			string randomName = groupNames[_random.Next(groupNames.Count)];
			_expressionStore.Toggle(randomName);
		}
		else
		{
			PlayTapBodyOrRandomMotion();
		}
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
