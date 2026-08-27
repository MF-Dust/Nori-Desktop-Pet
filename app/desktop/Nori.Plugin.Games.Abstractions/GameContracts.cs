namespace Nori.Plugin.Games.Abstractions;

/// <summary>游戏插件的只读描述。</summary>
public sealed record GameDefinition(string Id, string DisplayName, string Version);

/// <summary>游戏插件注册接口。</summary>
public interface IGameRegistry
{
	void Register(GameDefinition game);
	bool Remove(string gameId);
	IReadOnlyCollection<GameDefinition> Games { get; }
}

/// <summary>游戏插件提供的最小会话接口。</summary>
public interface IGameSession
{
	string GameId { get; }
	ValueTask StartAsync(CancellationToken cancellationToken = default);
	ValueTask StopAsync(CancellationToken cancellationToken = default);
}
