using Nori.Core.Data;

namespace Nori.Core.Memory;

/// <summary>知识表的基础仓储入口，供后续 KnowledgeService 扩展和测试装配使用。</summary>
public sealed class KnowledgeRepository
{
	private readonly NoriDatabase _database;

	public KnowledgeRepository(NoriDatabase database) => _database = database;

	public int CountChunks() => _database.Locked(connection =>
	{
		using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM knowledge_chunks";
		return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
	});
}
