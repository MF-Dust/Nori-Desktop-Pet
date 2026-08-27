using Nori.Plugin.Games.Abstractions;

namespace Nori.Plugin.Arcade.Abstractions;

/// <summary>街机游戏注册表。</summary>
public interface IArcadeRegistry : IGameRegistry
{
}

/// <summary>街机贡献。</summary>
public sealed record ArcadeGame(GameDefinition Definition, string? EntryPoint = null);
