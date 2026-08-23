using Nori.Core.Assets;
using Nori.Core.Chat;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Logging;
using Nori.Core.Resources;
using Nori.Core.Telemetry;
using Nori.Desktop.Live2D;
using Nori.Desktop.Windows;

namespace Nori.Desktop.Bridge;

/// <summary>
/// 应用级服务容器
///
/// 承接原来 Rust 侧 tauri::State 的角色: 把数据库/配置/资源/聊天/日志/窗口
/// 装配在一起交给桥接命令使用.
/// </summary>
public sealed class AppServices : IAsyncDisposable
{
	/// <summary>数据库</summary>
	public required NoriDatabase Database { get; init; }

	/// <summary>配置读写</summary>
	public required ConfigStore Config { get; init; }

	/// <summary>日志</summary>
	public required FileLogger Logger { get; init; }

	/// <summary>错误与性能遥测; 未装配时为空实现</summary>
	public ITelemetry Telemetry { get; set; } = NoopTelemetry.Instance;

	/// <summary>资源管理</summary>
	public required ResourceManager Resources { get; init; }

	/// <summary>聊天</summary>
	public required ChatService Chat { get; init; }

	/// <summary>记忆存储</summary>
	public required Nori.Core.Memory.MemoryStore Memory { get; init; }

	/// <summary>Embedding 向量接口 (支持 BGE-M3 / OpenAI 规范)</summary>
	public required Nori.Core.Embedding.OpenAiEmbeddingAdapter Embedding { get; init; }

	/// <summary>LLM 接口</summary>
	public required LlmClient Llm { get; init; }

	/// <summary>MCP (Model Context Protocol) 管理器</summary>
	public required Nori.Core.Mcp.McpManager Mcp { get; init; }

	/// <summary>回环资源服务</summary>
	public AssetServer? Assets { get; init; }

	/// <summary>本地/模型 HTTP 客户端 (测试可在装配后替换)</summary>
	public HttpClient Http { get; set; } = null!;

	private HttpClient? _publicHttp;

	/// <summary>公网 HTTP 客户端; 未显式装配时回退到 Http 以兼容测试装配。</summary>
	public HttpClient PublicHttp
	{
		get => _publicHttp ?? Http;
		set => _publicHttp = value;
	}

	/// <summary>Agent 聊天/MCP 操作取消注册表</summary>
	public required Bridge.AgentOperationRegistry AgentOperations { get; init; }

	/// <summary>窗口调度, 窗口建好后回填</summary>
	public IWindowManager Windows { get; set; } = null!;

	/// <summary>桥接命令, 服务装配完成后回填</summary>
	public BridgeCommands Commands { get; set; } = null!;

	/// <summary>原生 Live2D 桌宠运行时</summary>
	public PetRuntime PetRuntime { get; set; } = null!;

	/// <summary>应用业务运行时 (Agent/技能/情绪/提醒/语音), 窗口建好后回填</summary>
	public Runtime.AppRuntime? Runtime { get; set; }

	public async ValueTask DisposeAsync()
	{
		await Telemetry.FlushAsync(TimeSpan.FromSeconds(1));
		Telemetry.Dispose();
		await Mcp.DisposeAsync();
		if (Assets is not null) await Assets.DisposeAsync();
		if (_publicHttp is not null && !ReferenceEquals(_publicHttp, Http)) _publicHttp.Dispose();
		Http.Dispose();
		Database.Dispose();
	}
}
