namespace Nori.Desktop.Bridge;

public sealed record PluginCapabilityStatusDto(
	string Id,
	bool Declared,
	bool Granted,
	bool Available);

public sealed record PluginListItemDto(
	string Id,
	string Name,
	string Description,
	string Version,
	string Author,
	string? Homepage,
	string? Repository,
	string? License,
	string State,
	bool Enabled,
	IReadOnlyList<string> Capabilities,
	IReadOnlyList<string> OptionalCapabilities,
	IReadOnlyList<PluginCapabilityStatusDto> CapabilityStatuses,
	string? ErrorCode,
	string? ErrorMessage,
	bool RequiresRestart,
	string? IconUrl);

public sealed record PluginListResultDto(IReadOnlyList<PluginListItemDto> Plugins);

public sealed record PluginInstallResultDto(bool Cancelled, PluginListItemDto? Plugin);

public sealed record PluginUninstallResultDto(bool Success, bool RequiresRestart, PluginListItemDto? Plugin);
