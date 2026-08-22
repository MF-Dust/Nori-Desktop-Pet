using Live2DCSharpSDK.Framework.Model;
using Nori.Desktop.Live2D;

namespace Nori.Desktop.Live2D.Behaviors;

/// <summary>
/// 表情混合与衰减行为
///
/// 对应前端 plugins/expression.ts
/// - 支持 Add / Multiply / Overwrite 三种混合
/// - 逐帧更新并将未激活参数回落到模型默认值
/// </summary>
public sealed class ExpressionBehavior(ExpressionStore store) : IBehaviorPlugin
{
	private readonly ExpressionStore _store = store;
	private readonly HashSet<string> _activeLastFrame = [];

	/// <summary>
	/// 将后台准备好的表情定义同步应用到当前模型 (GL/渲染线程调用, 无 I/O)
	///
	/// entry 用当前 Cubism 模型的默认值建基线, 以新模型 ID 注册到共享 store。
	/// 不再存在 fire-and-forget 异步写入, 旧模型的表情任务没有机会污染新模型状态。
	/// </summary>
	public void ApplyPrepared(PreparedModel prepared, CubismModel model)
	{
		Dictionary<string, ExpressionEntry> entryMap = new(StringComparer.OrdinalIgnoreCase);
		foreach (ExpressionGroupDefinition group in prepared.ExpressionGroups)
		{
			foreach (ExpressionParameter parameter in group.Parameters)
			{
				if (entryMap.ContainsKey(parameter.ParameterId)) continue;
				float modelDefault = model.GetParameterDefaultValue(parameter.ParameterId);
				entryMap[parameter.ParameterId] = new ExpressionEntry
				{
					Name = parameter.ParameterId,
					ParameterId = parameter.ParameterId,
					Blend = parameter.Blend,
					CurrentValue = modelDefault,
					DefaultValue = modelDefault,
					ModelDefault = modelDefault,
					TargetValue = parameter.Value,
				};
			}
		}

		_store.RegisterExpressions(prepared.ModelId, prepared.ExpressionGroups, entryMap.Values);
	}

	private static ExpressionBlendMode NormaliseBlend(string? raw) => raw?.ToLowerInvariant() switch
	{
		"add" => ExpressionBlendMode.Add,
		"multiply" => ExpressionBlendMode.Multiply,
		_ => ExpressionBlendMode.Overwrite,
	};

	private static bool IsNoopValue(ExpressionEntry entry) => entry.Blend switch
	{
		ExpressionBlendMode.Add => Math.Abs(entry.CurrentValue) < 0.0001f,
		ExpressionBlendMode.Multiply => Math.Abs(entry.CurrentValue - 1.0f) < 0.0001f,
		_ => Math.Abs(entry.CurrentValue - entry.ModelDefault) < 0.0001f,
	};

	private static float ComputeTargetValue(ExpressionEntry entry, CubismModel model) => entry.Blend switch
	{
		ExpressionBlendMode.Add => entry.ModelDefault + entry.CurrentValue,
		ExpressionBlendMode.Multiply => model.GetParameterValue(entry.ParameterId) * entry.CurrentValue,
		_ => entry.CurrentValue,
	};

	public void Execute(BehaviorContext ctx)
	{
		if (!ctx.ExpressionEnabled) return;

		HashSet<string> activeThisFrame = [];
		var model = ctx.Model.Model;

		foreach (var entry in _store.Expressions.Values)
		{
			if (IsNoopValue(entry)) continue;

			float blendedValue = ComputeTargetValue(entry, model);
			model.SetParameterValue(entry.ParameterId, blendedValue);
			activeThisFrame.Add(entry.ParameterId);
		}

		foreach (string paramId in _activeLastFrame)
		{
			if (!activeThisFrame.Contains(paramId) && _store.Expressions.TryGetValue(paramId, out var entry))
			{
				model.SetParameterValue(paramId, entry.ModelDefault);
			}
		}

		_activeLastFrame.Clear();
		foreach (string id in activeThisFrame) _activeLastFrame.Add(id);
	}
}
