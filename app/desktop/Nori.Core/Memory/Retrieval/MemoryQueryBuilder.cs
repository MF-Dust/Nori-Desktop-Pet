namespace Nori.Core.Memory;

/// <summary>将当前消息和最近对话压缩为可用于记忆检索的查询。</summary>
public static class MemoryQueryBuilder
{
	public const int DefaultRecentMessageCount = 8;
	public const int DefaultMaxCharacters = 2400;

	public static string Build(
		string currentMessage,
		IReadOnlyList<(string Role, string Content)> recentMessages,
		int maxRecentMessages = DefaultRecentMessageCount,
		int maxCharacters = DefaultMaxCharacters)
	{
		string current = currentMessage.Trim();
		if (recentMessages.Count == 0) return Trim(current, maxCharacters);

		IEnumerable<(string Role, string Content)> recent = recentMessages
			.Where(message => !string.IsNullOrWhiteSpace(message.Content))
			.TakeLast(Math.Max(0, maxRecentMessages));
		string context = string.Join("\n", recent.Select(message => $"{message.Role}: {message.Content.Trim()}"));
		if (context.Length == 0) return Trim(current, maxCharacters);

		return Trim($"最近对话:\n{context}\n当前消息:\n{current}", maxCharacters);
	}

	private static string Trim(string value, int maxCharacters)
	{
		int max = Math.Max(128, maxCharacters);
		return value.Length <= max ? value : value[^max..];
	}
}
