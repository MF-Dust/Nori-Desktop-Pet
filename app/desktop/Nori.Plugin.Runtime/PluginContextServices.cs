using System.Text.Json;
using Nori.Plugin.Abstractions;

namespace Nori.Plugin.Runtime;

internal sealed class PluginContributionRegistry : IPluginContributionRegistry, IPluginContributions
{
	private readonly object _gate = new();
	private readonly Dictionary<string, PluginContribution> _items = new(StringComparer.Ordinal);
	public IReadOnlyCollection<PluginContribution> Items { get { lock (_gate) return _items.Values.ToArray(); } }
	void IPluginContributionRegistry.Register(PluginContribution contribution)
	{
		if (string.IsNullOrWhiteSpace(contribution.Id)) throw new ArgumentException("贡献 ID 不能为空", nameof(contribution));
		lock (_gate) _items[contribution.Id] = contribution;
	}
	public bool Remove(string id) { lock (_gate) return _items.Remove(id); }
}

internal sealed class PluginUiProviderRegistry : IPluginUiProviderRegistry
{
	private readonly object _gate = new();
	private readonly Dictionary<string, object> _providers = new(StringComparer.Ordinal);
	public void Register(string providerId, object provider)
	{
		if (string.IsNullOrWhiteSpace(providerId)) throw new ArgumentException("provider ID 不能为空", nameof(providerId));
		ArgumentNullException.ThrowIfNull(provider);
		lock (_gate) _providers[providerId] = provider;
	}
}

internal sealed class PluginCapabilityRegistry : IPluginCapabilityRegistry
{
	private readonly object _gate = new();
	private readonly Dictionary<string, PluginCapability> _items = new(StringComparer.Ordinal);
	public IReadOnlyCollection<PluginCapability> Items { get { lock (_gate) return _items.Values.ToArray(); } }
	public void Register(PluginCapability capability)
	{
		if (string.IsNullOrWhiteSpace(capability.Name)) throw new ArgumentException("能力名称不能为空", nameof(capability));
		lock (_gate) _items[capability.Name] = capability;
	}
	public bool Has(string name) { lock (_gate) return _items.ContainsKey(name); }
}

/// <summary>插件命名空间 JSON 存储，数据跨版本和卸载保留。</summary>
public sealed class JsonPluginStorage : IPluginStorage
{
	private readonly object _gate = new();
	private readonly string _path;
	private Dictionary<string, string> _values;

	public JsonPluginStorage(string directory)
	{
		Directory.CreateDirectory(directory);
		_path = Path.Combine(directory, "storage.json");
		_values = Load();
	}

	public IReadOnlyCollection<string> Keys { get { lock (_gate) return _values.Keys.ToArray(); } }
	public string? Get(string key) { ValidateKey(key); lock (_gate) return _values.GetValueOrDefault(key); }
	public void Set(string key, string value)
	{
		ValidateKey(key);
		ArgumentNullException.ThrowIfNull(value);
		lock (_gate) { _values[key] = value; Save(); }
	}
	public bool Remove(string key)
	{
		ValidateKey(key);
		lock (_gate) { bool removed = _values.Remove(key); if (removed) Save(); return removed; }
	}

	private Dictionary<string, string> Load()
	{
		if (!File.Exists(_path)) return new(StringComparer.Ordinal);
		try
		{
			Dictionary<string, string>? loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path));
			return loaded is null ? new(StringComparer.Ordinal) : new(loaded, StringComparer.Ordinal);
		}
		catch (JsonException exception) { throw new PluginException(PluginErrorCodes.PackageInvalid, "插件存储 JSON 无效", exception); }
	}

	private void Save()
	{
		string temporary = _path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(_values));
		File.Move(temporary, _path, true);
	}

	private static void ValidateKey(string key)
	{
		if (string.IsNullOrWhiteSpace(key) || key.Length > 256 || key.Contains('/') || key.Contains('\\') || key.Contains("..", StringComparison.Ordinal))
			throw new ArgumentException("存储键无效", nameof(key));
	}
}

/// <summary>只允许读取插件公开的 web/assets/locales/icon.png 资源。</summary>
public sealed class PluginAssetReader : IPluginAssetReader
{
	private readonly string _root;
	public PluginAssetReader(string root) => _root = Path.GetFullPath(root);
	public bool Exists(string relativePath) => Resolve(relativePath) is { } path && File.Exists(path);
	public Stream OpenRead(string relativePath)
	{
		string path = Resolve(relativePath) ?? throw new PluginException(PluginErrorCodes.AssetDenied, "插件资源路径不允许访问");
		return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
	}
	public IReadOnlyList<string> List(string? relativeDirectory = null)
	{
		string path = ResolveDirectory(relativeDirectory);
		if (!Directory.Exists(path)) return [];
		return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
			.Select(file => Path.GetRelativePath(_root, file).Replace(Path.DirectorySeparatorChar, '/'))
			.Where(IsPublicAsset).Order().ToArray();
	}
	private string? Resolve(string relativePath)
	{
		if (!IsPublicAsset(relativePath)) return null;
		string full = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
		if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
		return full;
	}
	private string ResolveDirectory(string? relativeDirectory) => relativeDirectory is null ? _root : Resolve(relativeDirectory + "/.directory") is { } path ? Path.GetDirectoryName(path)! : Path.Combine(_root, "__denied__");
	public static bool IsPublicAsset(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\') || path.Split('/').Any(part => part is "" or "." or "..")) return false;
		return path.Equals("icon.png", StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith("web/", StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith("locales/", StringComparison.OrdinalIgnoreCase);
	}
}
