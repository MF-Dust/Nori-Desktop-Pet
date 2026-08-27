using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Nori.Plugin.Runtime;

/// <summary>插件运行时错误码。</summary>
public static class PluginErrorCodes
{
	public const string InvalidManifest = "plugin.invalid_manifest";
	public const string IncompatibleVersion = "plugin.incompatible_version";
	public const string DuplicatePlugin = "plugin.duplicate";
	public const string MissingDependency = "plugin.missing_dependency";
	public const string DependencyCycle = "plugin.dependency_cycle";
	public const string ForbiddenReference = "plugin.forbidden_reference";
	public const string EntryTypeNotFound = "plugin.entry_type_not_found";
	public const string AssetDenied = "plugin.asset_denied";
	public const string PackageInvalid = "plugin.package_invalid";
	public const string LifecycleFailed = "plugin.lifecycle_failed";
}

/// <summary>插件异常，Code 是稳定的机器可读错误码。</summary>
public sealed class PluginException : Exception
{
	public PluginException(string code, string message) : base(message) => Code = code;
	public PluginException(string code, string message, Exception innerException) : base(message, innerException) => Code = code;
	public string Code { get; }
}

/// <summary>独立的三段版本，不把插件版本和 API 版本混为一谈。</summary>
public readonly record struct PluginVersion(int Major, int Minor, int Patch) : IComparable<PluginVersion>
{
	public static PluginVersion Parse(string value, string code = PluginErrorCodes.InvalidManifest)
	{
		if (!TryParse(value, out PluginVersion result)) throw new PluginException(code, $"版本格式无效: {value}");
		return result;
	}

	public static bool TryParse(string? value, out PluginVersion result)
	{
		result = default;
		if (string.IsNullOrWhiteSpace(value)) return false;
		string[] parts = value.Split('.', StringSplitOptions.None);
		if (parts.Length != 3 || parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit))) return false;
		if (!int.TryParse(parts[0], out int major) || !int.TryParse(parts[1], out int minor) || !int.TryParse(parts[2], out int patch)) return false;
		result = new PluginVersion(major, minor, patch);
		return true;
	}

	public int CompareTo(PluginVersion other) =>
		Major != other.Major ? Major.CompareTo(other.Major) : Minor != other.Minor ? Minor.CompareTo(other.Minor) : Patch.CompareTo(other.Patch);

	public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

/// <summary>插件依赖。</summary>
public sealed record PluginDependency(string PluginId, string MinVersion = "0.0.0");

/// <summary>清单身份与入口元数据，是插件身份和版本的唯一来源。</summary>
public sealed record PluginManifest
{
	public required string SchemaVersion { get; init; }
	public required string PluginId { get; init; }
	public required string Name { get; init; }
	public required string Version { get; init; }
	public required string ApiVersion { get; init; }
	public required string EntryAssembly { get; init; }
	public required string EntryType { get; init; }
	public IReadOnlyList<PluginDependency> Dependencies { get; init; } = [];
	public IReadOnlyList<string> Capabilities { get; init; } = [];

	public PluginVersion Schema => PluginVersion.Parse(SchemaVersion);
	public PluginVersion Plugin => PluginVersion.Parse(Version);
	public PluginVersion Api => PluginVersion.Parse(ApiVersion);
}

/// <summary>清单解析与兼容性规则。</summary>
public static partial class PluginManifestReader
{
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
	private static readonly Regex PluginIdPattern = PluginIdRegex();

	public static PluginManifest Read(string path)
	{
		try
		{
			using FileStream stream = File.OpenRead(path);
			using StreamReader reader = new(stream);
			PluginManifest? manifest = JsonSerializer.Deserialize<PluginManifest>(NormalizeJson(reader.ReadToEnd()), JsonOptions);
			return Validate(manifest ?? throw new PluginException(PluginErrorCodes.InvalidManifest, "插件清单为空"));
		}
		catch (PluginException) { throw; }
		catch (JsonException exception) { throw new PluginException(PluginErrorCodes.InvalidManifest, "插件清单 JSON 无效", exception); }
		catch (IOException exception) { throw new PluginException(PluginErrorCodes.InvalidManifest, "插件清单无法读取", exception); }
	}

	public static PluginManifest ReadJson(string json)
	{
		try
		{
			PluginManifest? manifest = JsonSerializer.Deserialize<PluginManifest>(NormalizeJson(json), JsonOptions);
			return Validate(manifest ?? throw new PluginException(PluginErrorCodes.InvalidManifest, "插件清单为空"));
		}
		catch (PluginException) { throw; }
		catch (JsonException exception) { throw new PluginException(PluginErrorCodes.InvalidManifest, "插件清单 JSON 无效", exception); }
	}

	public static PluginManifest Validate(PluginManifest manifest)
	{
		if (string.IsNullOrWhiteSpace(manifest.PluginId) || !PluginIdPattern.IsMatch(manifest.PluginId)) Invalid("插件 ID 无效");
		if (string.IsNullOrWhiteSpace(manifest.Name)) Invalid("插件名称不能为空");
		PluginVersion.Parse(manifest.SchemaVersion);
		PluginVersion.Parse(manifest.Version);
		PluginVersion.Parse(manifest.ApiVersion);
		if (string.IsNullOrWhiteSpace(manifest.EntryAssembly) || !IsSafeFileName(manifest.EntryAssembly) || !manifest.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) Invalid("入口程序集无效");
		if (string.IsNullOrWhiteSpace(manifest.EntryType) || manifest.EntryType.Contains(' ')) Invalid("入口类型无效");
		foreach (PluginDependency dependency in manifest.Dependencies)
		{
			if (!PluginIdPattern.IsMatch(dependency.PluginId)) Invalid("依赖插件 ID 无效");
			PluginVersion.Parse(dependency.MinVersion);
		}
		if (manifest.Capabilities.Any(capability => string.IsNullOrWhiteSpace(capability) || capability.Contains('/'))) Invalid("能力名称无效");
		return manifest with
		{
			Dependencies = manifest.Dependencies.Select(dependency => new PluginDependency(dependency.PluginId, dependency.MinVersion)).ToArray(),
			Capabilities = manifest.Capabilities.ToArray(),
		};
	}

	public static bool IsCompatible(PluginVersion host, PluginVersion plugin) => host.Major == plugin.Major && host.Minor >= plugin.Minor;

	public static void EnsureCompatible(PluginVersion hostSchema, PluginVersion pluginSchema, PluginVersion hostApi, PluginVersion pluginApi)
	{
		if (!IsCompatible(hostSchema, pluginSchema) || !IsCompatible(hostApi, pluginApi))
			throw new PluginException(PluginErrorCodes.IncompatibleVersion, $"插件版本不兼容: schema={pluginSchema}, api={pluginApi}");
	}

	private static string NormalizeJson(string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		if (document.RootElement.ValueKind != JsonValueKind.Object) return json;
		Dictionary<string, JsonElement> values = new(StringComparer.OrdinalIgnoreCase);
		foreach (JsonProperty property in document.RootElement.EnumerateObject())
			values[NormalizeName(property.Name)] = property.Value.Clone();
		return JsonSerializer.Serialize(values);
	}

	private static string NormalizeName(string name) => name switch
	{
		"schema_version" => "schemaVersion",
		"plugin_id" => "pluginId",
		"api_version" => "apiVersion",
		"entry_assembly" => "entryAssembly",
		"entry_type" => "entryType",
		_ => name,
	};

	private static void Invalid(string message) => throw new PluginException(PluginErrorCodes.InvalidManifest, message);
	private static bool IsSafeFileName(string value) => Path.GetFileName(value).Equals(value, StringComparison.Ordinal) && !value.Contains("..", StringComparison.Ordinal);
	[GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?$")]
	private static partial Regex PluginIdRegex();
}
