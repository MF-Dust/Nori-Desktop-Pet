using System.Text.Json;

namespace Nori.Core.Live2D;

/// <summary>
/// model3.json 元数据读取
///
/// 供设置页展示表情列表与动作组 (前端不再自行抓取模型 JSON 解析业务元数据)。
/// </summary>
public static class Model3Meta
{
	/// <summary>
	/// 读取模型目录下的元数据; 找不到或解析失败返回空结果
	/// </summary>
	public static Model3MetaInfo Read(string modelDir)
	{
		if (!Directory.Exists(modelDir)) return new Model3MetaInfo([], []);

		string? jsonPath = Directory.EnumerateFiles(modelDir, "*.model3.json", SearchOption.TopDirectoryOnly).FirstOrDefault();
		if (jsonPath is null) return new Model3MetaInfo([], []);

		try
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonPath));
			JsonElement root = document.RootElement;

			List<string> expressions = [];
			if (root.TryGetProperty("FileReferences", out JsonElement refs) && refs.TryGetProperty("Expressions", out JsonElement exps)
				&& exps.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item in exps.EnumerateArray())
				{
					if (item.TryGetProperty("Name", out JsonElement name) && name.ValueKind == JsonValueKind.String
						&& name.GetString() is {Length: > 0} expressionName)
					{
						expressions.Add(expressionName);
					}
				}
			}

			List<MotionGroupInfo> motions = [];
			if (root.TryGetProperty("FileReferences", out refs) && refs.TryGetProperty("Motions", out JsonElement motionRoot)
				&& motionRoot.ValueKind == JsonValueKind.Object)
			{
				foreach (JsonProperty group in motionRoot.EnumerateObject())
				{
					if (group.Value.ValueKind != JsonValueKind.Array) continue;
					List<string> names = [];
					foreach (JsonElement item in group.Value.EnumerateArray())
					{
						if (!item.TryGetProperty("File", out JsonElement file) || file.ValueKind != JsonValueKind.String) continue;
						string fileName = file.GetString() ?? "";
						int slash = fileName.LastIndexOf('/');
						if (slash >= 0) fileName = fileName[(slash + 1)..];
						if (fileName.EndsWith(".motion3.json", StringComparison.OrdinalIgnoreCase))
						{
							fileName = fileName[..^".motion3.json".Length];
						}
						if (fileName.Length > 0) names.Add(fileName);
					}
					if (names.Count > 0) motions.Add(new MotionGroupInfo {Group = group.Name, Names = names});
				}
			}

			return new Model3MetaInfo(expressions, motions);
		}
		catch (JsonException)
		{
			return new Model3MetaInfo([], []);
		}
	}
}

/// <summary>model3.json 元数据结果</summary>
public sealed record Model3MetaInfo(IReadOnlyList<string> Expressions, IReadOnlyList<MotionGroupInfo> Motions);
