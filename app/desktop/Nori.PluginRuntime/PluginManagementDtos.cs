namespace Nori.PluginRuntime;

internal sealed record PluginCapabilityStatusDto(
	string Id,
	bool Declared,
	bool Granted,
	bool Available);

internal sealed record PluginListItemDto(
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

internal sealed record PluginListResultDto(IReadOnlyList<PluginListItemDto> Plugins);

internal sealed record PluginInstallResultDto(bool Cancelled, PluginListItemDto? Plugin);

internal sealed record PluginUninstallResultDto(bool Success, bool RequiresRestart, PluginListItemDto? Plugin);
