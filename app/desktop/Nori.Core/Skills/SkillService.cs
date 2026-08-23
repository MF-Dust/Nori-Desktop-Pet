using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nori.Core.Configuration;
using Nori.Core.Network;
using YamlDotNet.Serialization;

namespace Nori.Core.Skills;

/// <summary>
/// 技能管理器服务
///
/// 支持本地技能、市场安装、URL 网络安装 (SKILL.md / JSON) 与 Prompt 动态注入。
/// 数据持久化沿用前端写入的 config 键 nori_skills (JSON 数组), 完全兼容既有数据。
/// </summary>
public sealed class SkillService(ConfigStore configStore, HttpClient httpClient)
{
	private const string ConfigKey = "nori_skills";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	private readonly Lock _gate = new();
	private Dictionary<string, SkillRecord> _skills = [];
	private bool _initialized;

	/// <summary>加载技能列表 (首次缺失时种子内置预设)</summary>
	public void EnsureLoaded()
	{
		lock (_gate)
		{
			if (_initialized) return;
			// 内置技能由程序集定义，配置只能恢复其 Enabled 状态，不能替换名称、指令或权限等内容。
			Dictionary<string, SkillRecord> loaded = SkillPresets.All
				.Where(preset => preset.Source == "builtin")
				.ToDictionary(preset => preset.Id, preset => SkillLimits.Normalize(preset));

			try
			{
				if (configStore.Get(ConfigKey) is ConfigValue.Json { Value: JsonNode node })
				{
					List<SkillRecord>? list = node.Deserialize<List<SkillRecord>>(JsonOptions);
					foreach (SkillRecord rawSkill in list ?? [])
					{
						if (string.IsNullOrWhiteSpace(rawSkill.Id)) continue;
						if (loaded.TryGetValue(rawSkill.Id, out SkillRecord? builtin))
						{
							// 只接受旧配置对内置技能的启停修改，拒绝覆盖内置定义。
							loaded[rawSkill.Id] = builtin with {Enabled = rawSkill.Enabled};
							continue;
						}
						SkillRecord normalized = SkillLimits.Normalize(rawSkill, remote: rawSkill.Source == "url");
						loaded[normalized.Id] = normalized;
					}
				}
			}
			catch (JsonException)
			{
				// 配置损坏时保留可信的内置预设, 不抛出避免阻断启动
			}

			_skills = loaded;
			_initialized = true;
			SaveLocked();
		}
	}

	/// <summary>获取所有已安装技能列表</summary>
	public IReadOnlyList<SkillRecord> GetInstalled()
	{
		EnsureLoaded();
		lock (_gate) return _skills.Values.ToList();
	}

	/// <summary>获取所有当前激活启用的技能列表</summary>
	public IReadOnlyList<SkillRecord> GetEnabled() => GetInstalled().Where(skill => skill.Enabled).ToList();

	/// <summary>获取技能市场目录</summary>
	public static IReadOnlyList<SkillRecord> Marketplace() => SkillPresets.All;

	/// <summary>切换技能启用状态</summary>
	public bool Toggle(string id, bool enabled)
	{
		EnsureLoaded();
		lock (_gate)
		{
			if (!_skills.TryGetValue(id, out SkillRecord? skill)) return false;
			skill.Enabled = enabled;
			SaveLocked();
			return true;
		}
	}

	/// <summary>从市场安装技能</summary>
	public SkillRecord InstallFromMarketplace(string skillId)
	{
		EnsureLoaded();
		SkillRecord? target = SkillPresets.Find(skillId)
			?? throw new InvalidOperationException($"未在市场中找到技能 ID: {skillId}");

		SkillRecord installed = SkillLimits.Normalize(target with
		{
			Enabled = true,
			InstalledAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
		});
		Upsert(installed);
		return installed;
	}

	/// <summary>创建或保存自定义技能</summary>
	public SkillRecord SaveCustom(SkillRecord skill)
	{
		EnsureLoaded();
		if (SkillPresets.Find(skill.Id) is not null)
		{
			throw new InvalidOperationException($"不能覆盖内置技能: {skill.Id}");
		}
		long existingAt;
		lock (_gate) existingAt = _skills.TryGetValue(skill.Id, out SkillRecord? old) ? old.InstalledAt : 0;
		SkillRecord complete = SkillLimits.Normalize(skill with
		{
			Source = "custom",
			Enabled = skill.Enabled,
			InstalledAt = existingAt != 0 ? existingAt : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
		});
		Upsert(complete);
		return complete;
	}

	/// <summary>卸载删除技能</summary>
	public bool Uninstall(string id)
	{
		EnsureLoaded();
		lock (_gate)
		{
			bool removed = _skills.Remove(id);
			if (removed) SaveLocked();
			return removed;
		}
	}

	/// <summary>导出技能为 JSON 字符串</summary>
	public string Export(string id)
	{
		EnsureLoaded();
		lock (_gate)
		{
			if (!_skills.TryGetValue(id, out SkillRecord? skill)) throw new InvalidOperationException("技能不存在");
			return JsonSerializer.Serialize(skill, JsonOptions);
		}
	}

	/// <summary>导入 JSON 技能</summary>
	public SkillRecord ImportJson(string json)
	{
		EnsureLoaded();
		SkillRecord data = JsonSerializer.Deserialize<SkillRecord>(json, JsonOptions)
			?? throw new InvalidOperationException("技能 JSON 解析失败");
		if (string.IsNullOrEmpty(data.Name) || string.IsNullOrEmpty(data.Instructions))
		{
			throw new InvalidOperationException("技能 JSON 缺少必要的 name 或 instructions 字段");
		}
		string id = string.IsNullOrEmpty(data.Id) ? $"custom_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" : data.Id;
		if (SkillPresets.Find(id) is not null)
		{
			throw new InvalidOperationException($"不能覆盖内置技能: {id}");
		}
		SkillRecord skill = SkillLimits.Normalize(data with
		{
			Id = id,
			Source = "custom",
			InstalledAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			Enabled = true,
		});
		Upsert(skill);
		return skill;
	}

	/// <summary>
	/// 从远程网络 URL 安装技能 (支持 JSON 规范与 SKILL.md Markdown 规范)
	/// </summary>
	public async Task<SkillRecord> InstallFromUrlAsync(string url, CancellationToken cancellationToken = default)
	{
		EnsureLoaded();
		Uri uri = new(url);
		Nori.Core.Network.UrlAccessPolicy.EnsurePublicHttp(uri);

		using HttpResponseMessage response = await Nori.Core.Network.UrlAccessPolicy.GetWithSafeRedirectsAsync(
			httpClient, uri, allowPrivate: false, cancellationToken: cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"下载技能失败: HTTP {(int)response.StatusCode}");
		}
		string text = await UrlAccessPolicy.ReadCappedTextAsync(
			response.Content, SkillLimits.MaxRemoteDocumentCharacters, cancellationToken);
		if (string.IsNullOrWhiteSpace(text))
		{
			throw new InvalidOperationException("远程技能文件内容为空");
		}

		SkillRecord skill = text.TrimStart().StartsWith("---") ? ParseSkillMarkdown(text, url) : ParseSkillJson(text, url);
		SkillRecord remote = SkillLimits.Normalize(skill with {Enabled = false, Source = "url", Url = url}, remote: true);
		Upsert(remote);
		return remote;
	}

	private SkillRecord ParseSkillJson(string text, string url)
	{
		SkillRecord manifest = JsonSerializer.Deserialize<SkillRecord>(text, JsonOptions)
			?? throw new InvalidOperationException("解析远程技能格式失败: 不是合法的 JSON 技能清单");
		return manifest with
		{
			Id = string.IsNullOrEmpty(manifest.Id) ? $"skill_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" : manifest.Id,
			Name = string.IsNullOrEmpty(manifest.Name) ? "未命名网络技能" : manifest.Name,
			Description = string.IsNullOrEmpty(manifest.Description) ? "从网络导入的技能" : manifest.Description,
			Author = string.IsNullOrEmpty(manifest.Author) ? "Online Author" : manifest.Author,
			Version = string.IsNullOrEmpty(manifest.Version) ? "1.0.0" : manifest.Version,
			Category = string.IsNullOrEmpty(manifest.Category) ? "productivity" : manifest.Category,
			Source = "url",
			Url = url,
		};
	}

	/// <summary>解析 SKILL.md (YAML Frontmatter + Markdown Instructions)</summary>
	public static SkillRecord ParseSkillMarkdown(string content, string url)
	{
		Match match = Regex.Match(content.TrimStart(), @"^---\r?\n(?<front>.*?)\r?\n---(?:\r?\n|$)(?<body>.*)$", RegexOptions.Singleline);
		if (!match.Success)
		{
			throw new InvalidOperationException("SKILL.md 格式错误：缺少完整的 YAML frontmatter 头部分隔符 ---");
		}

		Dictionary<string, string> meta;
		try
		{
			Dictionary<string, object?> parsed = new DeserializerBuilder()
				.IgnoreUnmatchedProperties()
				.Build()
				.Deserialize<Dictionary<string, object?>>(match.Groups["front"].Value) ?? [];
			meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach ((string key, object? value) in parsed)
			{
				if (value is null) continue;
				if (value is IEnumerable<object> sequence)
				{
					meta[key] = string.Join(",", sequence.Select(item => item?.ToString()).Where(item => !string.IsNullOrWhiteSpace(item)));
				}
				else
				{
					meta[key] = value.ToString() ?? "";
				}
			}
		}
		catch (YamlDotNet.Core.YamlException)
		{
			// 保持旧版对简单 frontmatter 的容错：YAML 不完整时按首个冒号切分。
			meta = ParseLegacyFrontmatter(match.Groups["front"].Value);
		}

		string body = match.Groups["body"].Value.Trim();
		string id = meta.TryGetValue("name", out string? name) && !string.IsNullOrEmpty(name)
			? Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9_-]+", "-")
			: $"skill_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

		meta.TryGetValue("tags", out string? tagsRaw);
		IReadOnlyList<string> tags = string.IsNullOrWhiteSpace(tagsRaw)
			? ["Skill"]
			: tagsRaw.Split(',').Select(tag => tag.Trim()).Where(tag => tag.Length > 0).ToList();

		return new SkillRecord
		{
			Id = id,
			Name = meta.GetValueOrDefault("name", "未命名技能"),
			Description = meta.GetValueOrDefault("description", "从 SKILL.md 安装的技能"),
			Author = meta.GetValueOrDefault("author", "Online Creator"),
			Version = meta.GetValueOrDefault("version", "1.0.0"),
			Icon = "sparkles",
			Tags = tags,
			Category = "productivity",
			Instructions = body,
			Enabled = false,
			Source = "url",
			InstalledAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			Url = url,
		};
	}

	private static Dictionary<string, string> ParseLegacyFrontmatter(string frontmatter)
	{
		Dictionary<string, string> meta = new(StringComparer.OrdinalIgnoreCase);
		foreach (string line in frontmatter.Split('\n'))
		{
			int colon = line.IndexOf(':');
			if (colon <= 0) continue;
			string key = line[..colon].Trim();
			string value = line[(colon + 1)..].Trim().Trim('"', '\'');
			if (key.Length > 0) meta[key] = value;
		}
		return meta;
	}

	/// <summary>
	/// 构建注入系统提示词的技能指令集
	/// </summary>
	public string BuildSkillsPrompt(IReadOnlySet<string>? availableTools = null)
	{
		IReadOnlyList<SkillRecord> active = GetEnabled().Where(skill => skill.Enabled).Take(SkillLimits.MaxSkills).ToList();
		if (active.Count == 0) return "";

		List<string> lines = ["【已激活技能与扩展指令 (Active Skills)】："];
		for (int i = 0; i < active.Count; i++)
		{
			SkillRecord skill = SkillLimits.Normalize(active[i], remote: active[i].Source == "url");
			List<string> entry = [$"\n=== 技能 {i + 1}：{skill.Name} (v{skill.Version}) ==="];
			if (!string.IsNullOrEmpty(skill.Description)) entry.Add($"简介: {skill.Description}");
			if (skill.Tools is {Count: > 0})
			{
				IEnumerable<string> available = availableTools is null ? skill.Tools : skill.Tools.Where(availableTools.Contains);
				IEnumerable<string> missing = availableTools is null ? [] : skill.Tools.Where(tool => !availableTools.Contains(tool));
				entry.Add($"Available tools: {string.Join(", ", available)}");
				if (missing.Any()) entry.Add($"Unavailable tools: {string.Join(", ", missing)}");
			}
			entry.Add(skill.Instructions);
			string candidate = string.Join("\n", lines.Append(string.Join("\n", entry)));
			if (candidate.Length > SkillLimits.MaxPromptCharacters) break;
			lines.Add(string.Join("\n", entry));
		}
		return SkillLimits.Cap(string.Join("\n", lines), SkillLimits.MaxPromptCharacters);
	}

	private void Upsert(SkillRecord skill)
	{
		SkillRecord normalized = SkillLimits.Normalize(skill, remote: skill.Source == "url");
		lock (_gate)
		{
			if (_skills.TryGetValue(normalized.Id, out SkillRecord? existing)
				&& existing.Source == "builtin"
				&& normalized.Source != "builtin")
			{
				throw new InvalidOperationException($"不能覆盖内置技能: {normalized.Id}");
			}
			_skills[normalized.Id] = normalized;
			SaveLocked();
		}
	}

	/// <summary>持久化已安装技能列表 (调用方需持有 _gate)</summary>
	private void SaveLocked()
	{
		string json = JsonSerializer.Serialize(_skills.Values.ToList(), JsonOptions);
		JsonNode? node = JsonNode.Parse(json);
		if (node is not null)
		{
			configStore.Set(ConfigKey, new ConfigValue.Json(node));
		}
	}
}
