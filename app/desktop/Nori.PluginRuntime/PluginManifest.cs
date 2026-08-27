using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace Nori.PluginRuntime;

/// <summary>插件运行时稳定错误码。</summary>
internal static class PluginErrorCodes
{
	public const string InvalidManifest = "plugin.invalid_manifest";
	public const string DuplicateManifestProperty = "plugin.duplicate_manifest_property";
	public const string UnknownSchema = "plugin.unknown_schema";
	public const string IncompatibleApi = "plugin.incompatible_api";
	public const string IncompatibleHost = "plugin.incompatible_host";
	public const string UnsupportedPlatform = "plugin.unsupported_platform";
	public const string UnknownCapability = "plugin.unknown_capability";
	public const string CapabilityMissing = "plugin.capability_missing";
	public const string CapabilityNotGranted = "plugin.capability_not_granted";
	public const string CapabilityUnavailable = "plugin.capability_unavailable";
	public const string MissingDependency = "plugin.missing_dependency";
	public const string DependencyCycle = "plugin.dependency_cycle";
	public const string InvalidDependency = "plugin.invalid_dependency";
	public const string DuplicateContribution = "plugin.duplicate_contribution";
	public const string InvalidPackage = "plugin.invalid_package";
	public const string PackagePathDenied = "plugin.package_path_denied";
	public const string ContractAssemblyDenied = "plugin.contract_assembly_denied";
	public const string ForbiddenReference = "plugin.forbidden_reference";
	public const string EntryAssemblyMissing = "plugin.entry_assembly_missing";
	public const string EntryTypeNotFound = "plugin.entry_type_not_found";
	public const string EntryConstructorMissing = "plugin.entry_constructor_missing";
	public const string ActivationFailed = "plugin.activation_failed";
	public const string DeactivationFailed = "plugin.deactivation_failed";
	public const string InvocationFailed = "plugin.invocation_failed";
	public const string UnloadPendingRestart = "plugin.unload_pending_restart";
	public const string AssetDenied = "plugin.asset_denied";
	public const string StorageFailed = "plugin.storage_failed";
	public const string BridgeDenied = "plugin.bridge_denied";
	public const string BridgeFailed = "plugin.bridge_failed";
	public const string SafeModeDisabled = "plugin.safe_mode_disabled";
	public const string StartupRecoveryDisabled = "plugin.startup_recovery_disabled";
	public const string InvalidPluginId = "plugin.invalid_id";
	public const string PluginNotFound = "plugin.not_found";
	public const string UserDisabled = "plugin.user_disabled";
	public const string DependencyInUse = "plugin.dependency_in_use";
	public const string UninstallPendingRestart = "plugin.uninstall_pending_restart";
}

/// <summary>SemVer 2.0 的最小不可变表示。</summary>
internal readonly record struct PluginVersion(
	int Major,
	int Minor,
	int Patch,
	string PreRelease = "",
	string Build = "") : IComparable<PluginVersion>
{
	public static PluginVersion Parse(string? value, string code = PluginErrorCodes.InvalidManifest)
	{
		if (!TryParse(value, out PluginVersion result))
			throw new PluginException(code, $"插件版本格式无效: {value}");
		return result;
	}

	public static bool TryParse(string? value, out PluginVersion result)
	{
		result = default;
		if (string.IsNullOrWhiteSpace(value)) return false;
		string[] buildSplit = value.Split('+', 2, StringSplitOptions.None);
		string[] preSplit = buildSplit[0].Split('-', 2, StringSplitOptions.None);
		string[] numeric = preSplit[0].Split('.', StringSplitOptions.None);
		if (numeric.Length != 3 || numeric.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit))) return false;
		if (!int.TryParse(numeric[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
			!int.TryParse(numeric[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
			!int.TryParse(numeric[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch)) return false;
		string preRelease = preSplit.Length == 2 ? preSplit[1] : "";
		string build = buildSplit.Length == 2 ? buildSplit[1] : "";
		if (!IsIdentifierList(preRelease, allowLeadingZeroNumeric: false) || !IsIdentifierList(build, allowLeadingZeroNumeric: true)) return false;
		result = new PluginVersion(major, minor, patch, preRelease, build);
		return true;
	}

	public int CompareTo(PluginVersion other)
	{
		int numeric = Major.CompareTo(other.Major);
		if (numeric != 0) return numeric;
		numeric = Minor.CompareTo(other.Minor);
		if (numeric != 0) return numeric;
		numeric = Patch.CompareTo(other.Patch);
		if (numeric != 0) return numeric;
		if (PreRelease.Length == 0) return other.PreRelease.Length == 0 ? 0 : 1;
		if (other.PreRelease.Length == 0) return -1;
		return CompareIdentifiers(PreRelease, other.PreRelease);
	}

	public override string ToString()
	{
		string value = $"{Major}.{Minor}.{Patch}";
		if (PreRelease.Length > 0) value += $"-{PreRelease}";
		if (Build.Length > 0) value += $"+{Build}";
		return value;
	}

	private static bool IsIdentifierList(string value, bool allowLeadingZeroNumeric)
	{
		if (value.Length == 0) return true;
		foreach (string identifier in value.Split('.', StringSplitOptions.None))
		{
			if (identifier.Length == 0 || !identifier.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')) return false;
			if (!allowLeadingZeroNumeric && identifier.All(char.IsAsciiDigit) && identifier.Length > 1 && identifier[0] == '0') return false;
		}
		return true;
	}

	private static int CompareIdentifiers(string left, string right)
	{
		string[] leftParts = left.Split('.');
		string[] rightParts = right.Split('.');
		for (int index = 0; index < Math.Min(leftParts.Length, rightParts.Length); index++)
		{
			string leftPart = leftParts[index];
			string rightPart = rightParts[index];
			bool leftNumeric = leftPart.All(char.IsAsciiDigit);
			bool rightNumeric = rightPart.All(char.IsAsciiDigit);
			int comparison = leftNumeric && rightNumeric
				? CompareNumericIdentifier(leftPart, rightPart)
				: leftNumeric != rightNumeric
					? (leftNumeric ? -1 : 1)
					: string.CompareOrdinal(leftPart, rightPart);
			if (comparison != 0) return comparison;
		}
		return leftParts.Length.CompareTo(rightParts.Length);
	}

	private static int CompareNumericIdentifier(string left, string right)
	{
		left = left.TrimStart('0');
		right = right.TrimStart('0');
		if (left.Length == 0) left = "0";
		if (right.Length == 0) right = "0";
		return left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
	}
}

/// <summary>严格的插件 API major.minor 版本。</summary>
internal readonly record struct PluginApiVersion(int Major, int Minor)
{
	public static PluginApiVersion Parse(string? value)
	{
		if (!TryParse(value, out PluginApiVersion result))
			throw new PluginException(PluginErrorCodes.InvalidManifest, $"插件 API 版本格式无效: {value}");
		return result;
	}

	public static bool TryParse(string? value, out PluginApiVersion result)
	{
		result = default;
		if (string.IsNullOrWhiteSpace(value)) return false;
		string[] parts = value.Split('.', StringSplitOptions.None);
		if (parts.Length != 2 || parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit))) return false;
		if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
			!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor)) return false;
		result = new PluginApiVersion(major, minor);
		return true;
	}

	public override string ToString() => $"{Major}.{Minor}";
}

/// <summary>manifest 作者。</summary>
internal sealed record PluginAuthor
{
	public required string Name { get; init; }
	public string? Email { get; init; }
}

/// <summary>manifest 依赖。</summary>
internal sealed record PluginDependency
{
	public required string Id { get; init; }
	public required string Version { get; init; }
	public bool Optional { get; init; }
}

/// <summary>插件进程内运行时入口。</summary>
internal sealed record PluginRuntimeDescriptor
{
	public required string Kind { get; init; }
	public required string Assembly { get; init; }
	public required string EntryType { get; init; }
}

/// <summary>插件 Web 资源根目录。</summary>
internal sealed record PluginUiDescriptor
{
	public required string WebRoot { get; init; }
}

/// <summary>manifest.json 的不可变模型。</summary>
internal sealed record PluginManifest
{
	public required int SchemaVersion { get; init; }
	public required string Id { get; init; }
	public required string Name { get; init; }
	public required string Description { get; init; }
	public required string Version { get; init; }
	public IReadOnlyList<PluginAuthor> Authors { get; init; } = [];
	public string? Homepage { get; init; }
	public string? Repository { get; init; }
	public string? License { get; init; }
	public required string ApiVersion { get; init; }
	public required string MinHostVersion { get; init; }
	public required PluginRuntimeDescriptor Runtime { get; init; }
	public PluginUiDescriptor? Ui { get; init; }
	public IReadOnlyList<string> Capabilities { get; init; } = [];
	public IReadOnlyList<string> OptionalCapabilities { get; init; } = [];
	public IReadOnlyList<string> Platforms { get; init; } = [];
	public IReadOnlyList<PluginDependency> Dependencies { get; init; } = [];
	[JsonIgnore]
	public PluginVersion PluginVersion => PluginVersion.Parse(Version);
	[JsonIgnore]
	public PluginApiVersion Api => PluginApiVersion.Parse(ApiVersion);
}

/// <summary>manifest 读取与校验。</summary>
internal static partial class PluginManifestReader
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		ReadCommentHandling = JsonCommentHandling.Disallow,
	};
	private static readonly Regex IdPattern = IdRegex();
	private static readonly HashSet<string> PlatformNames = new(StringComparer.Ordinal)
	{
		"windows", "linux", "macos",
	};

	public static PluginManifest Read(string path)
	{
		try
		{
			return ReadJson(File.ReadAllText(path));
		}
		catch (PluginException)
		{
			throw;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
		{
			throw new PluginException(PluginErrorCodes.InvalidManifest, "manifest.json 无法读取", exception);
		}
	}

	public static PluginManifest ReadJson(string json)
	{
		if (json is null) throw new ArgumentNullException(nameof(json));
		try
		{
			using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
			{
				CommentHandling = JsonCommentHandling.Disallow,
				MaxDepth = 32,
			});
			if (document.RootElement.ValueKind != JsonValueKind.Object) Invalid("manifest.json 根节点必须是对象");
			EnsureNoDuplicateProperties(document.RootElement);
			PluginManifest? manifest = JsonSerializer.Deserialize<PluginManifest>(json, JsonOptions);
			return Validate(manifest ?? throw new PluginException(PluginErrorCodes.InvalidManifest, "manifest.json 为空"));
		}
		catch (PluginException)
		{
			throw;
		}
		catch (JsonException exception)
		{
			throw new PluginException(PluginErrorCodes.InvalidManifest, "manifest.json JSON 无效", exception);
		}
		catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or OverflowException)
		{
			throw new PluginException(PluginErrorCodes.InvalidManifest, "manifest.json 结构无效", exception);
		}
	}

	public static PluginManifest Validate(PluginManifest manifest)
	{
		ArgumentNullException.ThrowIfNull(manifest);
		if (manifest.SchemaVersion != 1) throw new PluginException(PluginErrorCodes.UnknownSchema, "不支持的 manifest schemaVersion");
		if (string.IsNullOrWhiteSpace(manifest.Id) || manifest.Id.Length > 128 || !IdPattern.IsMatch(manifest.Id)) Invalid("插件 ID 无效");
		if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 256) Invalid("插件名称无效");
		if (string.IsNullOrWhiteSpace(manifest.Description) || manifest.Description.Length > 4096) Invalid("插件描述无效");
		PluginVersion.Parse(manifest.Version);
		PluginApiVersion.Parse(manifest.ApiVersion);
		PluginVersion.Parse(manifest.MinHostVersion, PluginErrorCodes.InvalidManifest);
		IReadOnlyList<PluginAuthor> authors = manifest.Authors ?? [];
		IReadOnlyList<string> capabilities = manifest.Capabilities ?? [];
		IReadOnlyList<string> optionalCapabilities = manifest.OptionalCapabilities ?? [];
		IReadOnlyList<string> platforms = manifest.Platforms ?? [];
		IReadOnlyList<PluginDependency> dependencies = manifest.Dependencies ?? [];
		PluginRuntimeDescriptor runtime = manifest.Runtime ?? throw new PluginException(PluginErrorCodes.InvalidManifest, "runtime 不能为空");
		if (!string.Equals(runtime.Kind, "dotnet", StringComparison.Ordinal)) Invalid("runtime.kind 必须为 dotnet");
		if (!IsPluginAssemblyPath(runtime.Assembly)) Invalid("runtime.assembly 必须是 lib/ 下的 DLL");
		if (string.IsNullOrWhiteSpace(runtime.EntryType) || runtime.EntryType.Contains(',', StringComparison.Ordinal) || runtime.EntryType.Contains(' ')) Invalid("runtime.entryType 无效");
		if (manifest.Ui is not null && !IsWebRoot(manifest.Ui.WebRoot)) Invalid("ui.webRoot 必须位于 web/ 下");
		ValidateAuthors(authors);
		ValidateCapabilityList(capabilities, "capabilities");
		ValidateCapabilityList(optionalCapabilities, "optionalCapabilities");
		if (capabilities.Intersect(optionalCapabilities, StringComparer.Ordinal).Any()) Invalid("required 与 optional capability 重复");
		ValidateDistinct(platforms, "platforms");
		foreach (string platform in platforms)
			if (!PlatformNames.Contains(platform)) Invalid($"不支持的平台: {platform}");
		HashSet<string> dependencyIds = new(StringComparer.Ordinal);
		foreach (PluginDependency? dependency in dependencies)
		{
			PluginDependency item = dependency ?? throw new PluginException(PluginErrorCodes.InvalidDependency, "dependencies 包含空项");
			if (string.IsNullOrWhiteSpace(item.Id) || !IdPattern.IsMatch(item.Id) || !dependencyIds.Add(item.Id)) Invalid("dependencies 无效或重复");
			if (!PluginRange.TryParse(item.Version, out _))
				throw new PluginException(PluginErrorCodes.InvalidDependency, $"依赖版本范围无效: {item.Id}");
		}
		return manifest with
		{
			Authors = authors.ToArray(),
			Capabilities = capabilities.ToArray(),
			OptionalCapabilities = optionalCapabilities.ToArray(),
			Platforms = platforms.ToArray(),
			Dependencies = dependencies.Select(dependency => dependency with { }).ToArray(),
		};
	}

	/// <summary>判断 host API 是否满足插件 API。</summary>
	public static bool IsCompatible(PluginApiVersion host, PluginApiVersion plugin) =>
		host.Major == plugin.Major && host.Minor >= plugin.Minor;

	public static void EnsureCompatible(PluginApiVersion host, PluginApiVersion plugin)
	{
		if (!IsCompatible(host, plugin))
			throw new PluginException(PluginErrorCodes.IncompatibleApi, $"插件 API 不兼容: host={host}, plugin={plugin}");
	}

	public static bool IsValidPluginId(string? id) =>
		!string.IsNullOrWhiteSpace(id) && id.Length <= 128 && IdPattern.IsMatch(id);

	public static bool IsHostVersionSupported(PluginVersion host, string minimum)
	{
		return PluginVersion.TryParse(minimum, out PluginVersion required) && host.CompareTo(required) >= 0;
	}

	public static bool IsPlatformSupported(IReadOnlyList<string> platforms)
	{
		if (platforms.Count == 0) return true;
		string current = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : OperatingSystem.IsMacOS() ? "macos" : "unknown";
		return platforms.Contains(current, StringComparer.Ordinal);
	}

	private static void ValidateAuthors(IReadOnlyList<PluginAuthor> authors)
	{
		foreach (PluginAuthor author in authors)
			if (author is null || string.IsNullOrWhiteSpace(author.Name) || author.Name.Length > 256) Invalid("authors 无效");
	}

	private static void ValidateCapabilityList(IReadOnlyList<string> values, string field)
	{
		ValidateDistinct(values, field);
		foreach (string value in values)
			if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(character => char.IsControl(character) || character is '/' or '\\')) Invalid($"{field} 无效");
	}

	private static void ValidateDistinct(IReadOnlyList<string> values, string field)
	{
		if (values.Any(string.IsNullOrWhiteSpace) || values.Count != values.Distinct(StringComparer.Ordinal).Count()) Invalid($"{field} 存在空值或重复项");
	}

	private static bool IsPluginAssemblyPath(string? path) =>
		!string.IsNullOrWhiteSpace(path) && path.StartsWith("lib/", StringComparison.Ordinal) &&
		path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && IsSafeRelativePath(path);

	private static bool IsWebRoot(string? path) =>
		!string.IsNullOrWhiteSpace(path) && (path.Equals("web", StringComparison.Ordinal) || path.StartsWith("web/", StringComparison.Ordinal)) && IsSafeRelativePath(path);

	private static bool IsSafeRelativePath(string path)
	{
		if (path.Length == 0 || path[0] is '/' or '\\' || Path.IsPathRooted(path)) return false;
		if (path.Contains('\\') || path.Contains(':', StringComparison.Ordinal) || path.Any(char.IsControl)) return false;
		return path.Split('/').All(part => part.Length > 0 && part is not "." and not "..");
	}

	private static void EnsureNoDuplicateProperties(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
			foreach (JsonProperty property in element.EnumerateObject())
			{
				if (!names.Add(property.Name)) throw new PluginException(PluginErrorCodes.DuplicateManifestProperty, $"manifest.json 存在重复字段: {property.Name}");
				EnsureNoDuplicateProperties(property.Value);
			}
		}
		else if (element.ValueKind == JsonValueKind.Array)
			foreach (JsonElement item in element.EnumerateArray()) EnsureNoDuplicateProperties(item);
	}

	private static void Invalid(string message) => throw new PluginException(PluginErrorCodes.InvalidManifest, message);

	[GeneratedRegex("^[a-z0-9]+(\\.[a-z0-9_-]+)+$")]
	private static partial Regex IdRegex();
}
