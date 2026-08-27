using System.Text.Json;
using Nori.Plugin.Abstractions;

namespace Nori.Plugin.Runtime;

/// <summary>持久化用户对插件的启用意图与需要重启后完成的卸载请求。</summary>
internal sealed class PluginStateStore
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
	};

	private readonly object _gate = new();
	private readonly string _path;
	private Dictionary<string, PluginRuntimeState> _states;

	public PluginStateStore(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		_path = Path.GetFullPath(path);
		Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
		_states = Load(_path);
	}

	/// <summary>既有插件没有记录时保持 1.0 兼容语义，默认视为启用。</summary>
	public bool IsEnabled(string pluginId)
	{
		ValidateId(pluginId);
		lock (_gate)
			return !_states.TryGetValue(pluginId, out PluginRuntimeState? state) || state.Enabled;
	}

	public void SetEnabled(string pluginId, bool enabled)
	{
		ValidateId(pluginId);
		lock (_gate)
		{
			_states.TryGetValue(pluginId, out PluginRuntimeState? previous);
			_states[pluginId] = (previous ?? new PluginRuntimeState()) with { Enabled = enabled };
			Persist();
		}
	}

	public void SetPendingUninstall(string pluginId, bool deleteData)
	{
		ValidateId(pluginId);
		lock (_gate)
		{
			_states.TryGetValue(pluginId, out PluginRuntimeState? previous);
			_states[pluginId] = (previous ?? new PluginRuntimeState()) with
			{
				Enabled = false,
				PendingUninstall = true,
				DeleteData = deleteData,
			};
			Persist();
		}
	}

	public bool TryGetPendingUninstall(string pluginId, out bool deleteData)
	{
		ValidateId(pluginId);
		lock (_gate)
		{
			if (_states.TryGetValue(pluginId, out PluginRuntimeState? state) && state.PendingUninstall)
			{
				deleteData = state.DeleteData;
				return true;
			}
		}
		deleteData = false;
		return false;
	}

	public IReadOnlyList<(string PluginId, bool DeleteData)> PendingUninstalls()
	{
		lock (_gate)
		{
			return _states
				.Where(pair => pair.Value.PendingUninstall && PluginManifestReader.IsValidPluginId(pair.Key))
				.OrderBy(pair => pair.Key, StringComparer.Ordinal)
				.Select(pair => (pair.Key, pair.Value.DeleteData))
				.ToArray();
		}
	}

	public void ClearPendingUninstall(string pluginId)
	{
		ValidateId(pluginId);
		lock (_gate)
		{
			if (!_states.TryGetValue(pluginId, out PluginRuntimeState? state) || !state.PendingUninstall) return;
			_states[pluginId] = state with { PendingUninstall = false, DeleteData = false };
			Persist();
		}
	}

	public void Remove(string pluginId)
	{
		ValidateId(pluginId);
		lock (_gate)
		{
			if (!_states.Remove(pluginId)) return;
			Persist();
		}
	}

	private void Persist()
	{
		string temporary = _path + ".tmp-" + Guid.NewGuid().ToString("N");
		try
		{
			File.WriteAllText(temporary, JsonSerializer.Serialize(_states, JsonOptions));
			File.Move(temporary, _path, true);
		}
		finally
		{
			try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
		}
	}

	private static Dictionary<string, PluginRuntimeState> Load(string path)
	{
		if (!File.Exists(path)) return new(StringComparer.Ordinal);
		try
		{
			Dictionary<string, PluginRuntimeState>? states = JsonSerializer.Deserialize<Dictionary<string, PluginRuntimeState>>(File.ReadAllText(path), JsonOptions);
			return states is null ? new(StringComparer.Ordinal) : new(states, StringComparer.Ordinal);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
		{
			return new(StringComparer.Ordinal);
		}
	}

	private static void ValidateId(string pluginId)
	{
		if (!PluginManifestReader.IsValidPluginId(pluginId))
			throw new PluginException(PluginErrorCodes.InvalidManifest, "插件 ID 无效");
	}

	private sealed record PluginRuntimeState
	{
		public bool Enabled { get; init; } = true;
		public bool PendingUninstall { get; init; }
		public bool DeleteData { get; init; }
	}
}
