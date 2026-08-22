using System.Collections.Concurrent;

namespace Nori.Desktop.Bridge;

/// <summary>
/// Agent 操作取消注册表
///
/// 以 (来源窗口, session ID) 为键登记正在运行的聊天/MCP CancellationTokenSource,
/// cancel_agent_session 只能取消同一来源窗口登记的操作.
/// 注册表只保存活跃 CTS: 完成/异常/取消三种路径都会解除登记并释放资源.
/// </summary>
public sealed class AgentOperationRegistry
{
	private readonly record struct Key(string SourceLabel, string SessionId);

	private readonly ConcurrentDictionary<Key, CancellationTokenSource> _operations = [];

	/// <summary>
	/// 登记一个与 linkedToken 关联的取消源; 同键重复登记会先解除旧登记.
	/// </summary>
	public CancellationTokenSource Register(string sourceLabel, string sessionId, CancellationToken linkedToken)
	{
		CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
		Key key = new(sourceLabel, sessionId);
		if (_operations.TryRemove(key, out CancellationTokenSource? previous))
		{
			previous.Cancel();
			previous.Dispose();
		}
		_operations[key] = cts;
		return cts;
	}

	/// <summary>解除登记并释放 CTS (幂等).</summary>
	public void Complete(string sourceLabel, string sessionId, CancellationTokenSource cts)
	{
		Key key = new(sourceLabel, sessionId);
		if (_operations.TryRemove(new KeyValuePair<Key, CancellationTokenSource>(key, cts)))
		{
			cts.Dispose();
		}
	}

	/// <summary>
	/// 取消指定来源窗口的活动操作; 来源不匹配或不存在时返回 false.
	/// </summary>
	public bool TryCancel(string sourceLabel, string sessionId)
	{
		if (!_operations.TryGetValue(new Key(sourceLabel, sessionId), out CancellationTokenSource? cts)) return false;
		cts.Cancel();
		return true;
	}
}
