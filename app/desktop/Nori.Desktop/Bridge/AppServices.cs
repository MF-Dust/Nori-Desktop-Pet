using Nori.Core.Assets;
using Nori.Core.Chat;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Logging;
using Nori.Core.Resources;
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

	/// <summary>资源管理</summary>
	public required ResourceManager Resources { get; init; }

	/// <summary>聊天</summary>
	public required ChatService Chat { get; init; }

	/// <summary>LLM 接口</summary>
	public required LlmClient Llm { get; init; }

	/// <summary>回环资源服务</summary>
	public required AssetServer Assets { get; init; }

	/// <summary>HTTP 客户端 (全应用共用一个)</summary>
	public required HttpClient Http { get; init; }

	/// <summary>窗口调度, 窗口建好后回填</summary>
	public WindowManager Windows { get; set; } = null!;

	/// <summary>桥接命令, 服务装配完成后回填</summary>
	public BridgeCommands Commands { get; set; } = null!;

	public async ValueTask DisposeAsync()
	{
		await Assets.DisposeAsync();
		Http.Dispose();
		Database.Dispose();
	}
}
