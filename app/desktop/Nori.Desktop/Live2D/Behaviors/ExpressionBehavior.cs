using System.Text.Json;
using Live2DCSharpSDK.App;
using Live2DCSharpSDK.Framework.Model;

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

	public async Task InitializeAsync(string modelHomeDir, IEnumerable<(string Name, string File)> expressionRefs, CubismModel model)
	{
		_store.Dispose();
		_activeLastFrame.Clear();

		List<ExpressionGroupDefinition> groups = [];
		Dictionary<string, ExpressionEntry> entryMap = new(StringComparer.OrdinalIgnoreCase);

		foreach (var (name, file) in expressionRefs)
		{
			string filePath = Path.Combine(modelHomeDir, file);
			if (!File.Exists(filePath)) continue;

			try
			{
				string json = await File.ReadAllTextAsync(filePath);
				var expFile = JsonSerializer.Deserialize<Exp3JsonFile>(json);
				if (expFile?.Parameters == null) continue;

				List<ExpressionParameter> groupParams = [];
				foreach (var p in expFile.Parameters)
				{
					if (string.IsNullOrEmpty(p.Id)) continue;
					var blend = NormaliseBlend(p.Blend);
					groupParams.Add(new ExpressionParameter
					{
						ParameterId = p.Id,
						Blend = blend,
						Value = p.Value,
					});

					if (!entryMap.ContainsKey(p.Id))
					{
						float modelDefault = model.GetParameterDefaultValue(p.Id);
						entryMap[p.Id] = new ExpressionEntry
						{
							Name = p.Id,
							ParameterId = p.Id,
							Blend = blend,
							CurrentValue = modelDefault,
							DefaultValue = modelDefault,
							ModelDefault = modelDefault,
							TargetValue = p.Value,
						};
					}
				}

				groups.Add(new ExpressionGroupDefinition
				{
					Name = name,
					Parameters = groupParams,
				});
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Live2D] Failed to load exp file {file}: {ex.Message}");
			}
		}

		_store.RegisterExpressions(_store.ModelId, groups, entryMap.Values);
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
