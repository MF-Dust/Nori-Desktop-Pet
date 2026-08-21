using Live2DCSharpSDK.App;

namespace Nori.Desktop.Live2D.Behaviors;

/// <summary>
/// 行为执行上下文
/// </summary>
public sealed class BehaviorContext
{
	public required LAppModel Model { get; init; }
	public required double Now { get; init; }
	public required double TimeDelta { get; init; }
	public required bool IsIdleMotion { get; init; }
	public bool EyeTrackingEnabled { get; set; } = true;
	public bool EyeFocusSourceActive { get; set; }
	public bool IdleEyeAnimationEnabled { get; set; } = true;
	public bool IdleAnimationEnabled { get; set; } = true;
	public bool ForceIdleEyeAnimation { get; set; } = true;
	public bool AutoBlinkEnabled { get; set; } = true;
	public bool ForceAutoBlinkEnabled { get; set; } = true;
	public bool BeatSyncEnabled { get; set; }
	public bool LipSyncEnabled { get; set; } = true;
	public bool ExpressionEnabled { get; set; } = true;
	public bool ClickInteraction { get; set; } = true;
	public ModelParameters ModelParameters { get; set; } = new();
	public bool Handled { get; private set; }

	public void MarkHandled() => Handled = true;
}

public enum PipelineStage
{
	Pre,
	Post,
	Final,
}

public interface IBehaviorPlugin
{
	void Execute(BehaviorContext ctx);
}

/// <summary>
/// 行为管线调度器
///
/// 对应前端 plugins/index.ts（pre / post / final 三段 + handled 短路）
/// </summary>
public sealed class BehaviorPipeline
{
	private readonly List<IBehaviorPlugin> _prePlugins = [];
	private readonly List<IBehaviorPlugin> _postPlugins = [];
	private readonly List<IBehaviorPlugin> _finalPlugins = [];

	public void Register(IBehaviorPlugin plugin, PipelineStage stage = PipelineStage.Pre)
	{
		switch (stage)
		{
			case PipelineStage.Pre:
				_prePlugins.Add(plugin);
				break;
			case PipelineStage.Post:
				_postPlugins.Add(plugin);
				break;
			case PipelineStage.Final:
				_finalPlugins.Add(plugin);
				break;
		}
	}

	public void RunPre(BehaviorContext ctx)
	{
		foreach (IBehaviorPlugin plugin in _prePlugins)
		{
			if (ctx.Handled) break;
			plugin.Execute(ctx);
		}
	}

	public void RunPost(BehaviorContext ctx)
	{
		foreach (IBehaviorPlugin plugin in _postPlugins)
		{
			if (ctx.Handled) break;
			plugin.Execute(ctx);
		}
	}

	public void RunFinal(BehaviorContext ctx)
	{
		foreach (IBehaviorPlugin plugin in _finalPlugins)
		{
			plugin.Execute(ctx);
		}
	}
}
