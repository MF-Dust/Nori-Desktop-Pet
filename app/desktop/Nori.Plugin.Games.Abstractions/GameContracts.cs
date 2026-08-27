using System.Text.Json;
using Nori.Plugin.Abstractions;

namespace Nori.Plugin.Games.Abstractions;

/// <summary>游戏提供者描述。</summary>
public sealed record GameDescriptor
{
	public required string Id { get; init; }
	public required string Name { get; init; }
	public required string Description { get; init; }
	public string? IconPath { get; init; }
}

/// <summary>宿主启动游戏会话时传递的上下文。</summary>
public sealed record GameLaunchContext
{
	public required string SessionId { get; init; }
	public JsonElement Arguments { get; init; }
}

/// <summary>游戏插件贡献。</summary>
public interface IGameProvider : IPluginContribution
{
	GameDescriptor Descriptor { get; }

	ValueTask<IGameSession> CreateSessionAsync(
		GameLaunchContext context,
		CancellationToken cancellationToken);
}

/// <summary>游戏会话生命周期。</summary>
public interface IGameSession : IAsyncDisposable
{
	ValueTask StartAsync(CancellationToken cancellationToken);
	ValueTask StopAsync(CancellationToken cancellationToken);
}
