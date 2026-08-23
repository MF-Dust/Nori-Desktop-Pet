using System.Globalization;

namespace Nori.Core.Memory;

/// <summary>执行轻量衰减、过期和归档维护。</summary>
public sealed class MemoryLifecycleService
{
	private readonly MemoryService _memory;

	public MemoryLifecycleService(MemoryService memory) => _memory = memory;

	public int RunOnce(DateTimeOffset? now = null)
	{
		MemorySettings settings = _memory.Settings;
		if (!settings.DecayEnabled) return 0;
		DateTimeOffset current = now ?? DateTimeOffset.UtcNow;
		int changed = 0;
		foreach (MemoryItem item in _memory.Store.GetAll(100000))
		{
			if (item.Status is "superseded" or "archived") continue;
			if (IsExpired(item, current))
			{
				if (item.Status != "expired" && _memory.Store.SetStatus(item.Id, MemoryStatus.Expired)) changed++;
				continue;
			}
			double score = DecayCalculator.TemporalScore(item, current);
			if (item.Status == "active" && score < 0.35 && _memory.Store.SetStatus(item.Id, MemoryStatus.Dormant)) changed++;
			else if (item.Status == "dormant" && settings.ArchiveEnabled && score < settings.ArchiveThreshold && _memory.Store.Archive(item.Id)) changed++;
		}
		foreach (MemoryAtom atom in _memory.Store.GetAtoms(null, null, 100000))
		{
			if (atom.Status is MemoryStatus.Superseded or MemoryStatus.Archived) continue;
			if (IsExpired(atom, current))
			{
				if (atom.Status != MemoryStatus.Expired && _memory.Store.SetAtomStatus(atom.Id, MemoryStatus.Expired)) changed++;
				continue;
			}
			double score = DecayCalculator.TemporalScore(atom, current);
			if (atom.Status == MemoryStatus.Active && score < 0.35 && _memory.Store.SetAtomStatus(atom.Id, MemoryStatus.Dormant)) changed++;
			else if (atom.Status == MemoryStatus.Dormant && settings.ArchiveEnabled && score < settings.ArchiveThreshold && _memory.Store.SetAtomStatus(atom.Id, MemoryStatus.Archived)) changed++;
		}
		_memory.Store.SetEngineState("last_maintenance_at", current.ToString("o", CultureInfo.InvariantCulture));
		return changed;
	}

	private static bool IsExpired(MemoryAtom atom, DateTimeOffset now)
	{
		return !string.IsNullOrWhiteSpace(atom.ExpiresAt)
			&& DateTimeOffset.TryParse(atom.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset expires)
			&& expires <= now;
	}

	private static bool IsExpired(MemoryItem item, DateTimeOffset now)
	{
		if (string.IsNullOrWhiteSpace(item.ExpiresAt)) return false;
		return DateTimeOffset.TryParse(item.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset expires)
			&& expires <= now;
	}
}
