namespace Nori.Core.Data;

/// <summary>
/// 旧 Tauri 数据路径兼容常量。
///
/// 新宿主禁止使用这些可写路径，统一使用显式注入的 <see cref="AppStoragePaths" />。
/// 这里仅保留旧测试、迁移源识别和兼容构造函数所需的常量及只读计算属性。
/// </summary>
public static class AppPaths
{
	/// <summary>应用标识，与旧 tauri.conf.json identifier 保持一致。</summary>
	public const string Identifier = "cn.erhio.noriDesktopPet";

	/// <summary>数据库文件名。</summary>
	public const string DatabaseFileName = "nori.db";

	/// <summary>旧资源目录名。</summary>
	public const string ResourcesDirName = "resources";

	/// <summary>旧目录，仅供 LegacyDataPathResolver 和兼容测试使用。</summary>
	public static string DataDir => LegacyDataPathResolver.Resolve();
	public static string DatabasePath => Path.Combine(DataDir, DatabaseFileName);
	public static string ResourcesDir => Path.Combine(DataDir, ResourcesDirName);
	public static string LogDir => Path.Combine(DataDir, "log");
	public static string KnowledgeDir => Path.Combine(DataDir, "knowledge");
	public static string MemoryMarkdownPath => Path.Combine(KnowledgeDir, "Memory.md");
}
