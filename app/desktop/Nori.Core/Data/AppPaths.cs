namespace Nori.Core.Data;

/// <summary>
/// 应用路径
///
/// 必须与 Tauri 版的 app_data_dir() 完全一致, 否则老用户的 nori.db 与本地模型会"丢失":
/// Windows: %APPDATA%/&lt;应用标识&gt;/data
/// macOS:   ~/Library/Application Support/&lt;应用标识&gt;/data
/// Linux:   ~/.local/share/&lt;应用标识&gt;/data
/// </summary>
public static class AppPaths
{
	private static string? _diagnosticProfile;

	/// <summary>
	/// 应用标识, 与 tauri.conf.json 的 identifier 保持一致
	/// </summary>
	public const string Identifier = "cn.erhio.noriDesktopPet";

	/// <summary>
	/// 数据库文件名
	/// </summary>
	public const string DatabaseFileName = "nori.db";

	/// <summary>
	/// 所有资源的根目录名
	/// </summary>
	public const string ResourcesDirName = "resources";

	/// <summary>
	/// 应用数据目录: &lt;平台数据目录&gt;/&lt;应用标识&gt;/data
	/// </summary>
	public static string DataDir => _diagnosticProfile is { } profile
		? Path.Combine(profile, "data")
		: Path.Combine(AppDataRoot(), Identifier, "data");

	/// <summary>
	/// 为隔离的启动冒烟模式设置 profile。
	/// 普通启动不会调用此方法, 真实用户目录因此保持不变。
	/// </summary>
	public static void UseDiagnosticProfile(string profile)
	{
		if (string.IsNullOrWhiteSpace(profile)) throw new ArgumentException("profile 不能为空", nameof(profile));
		string fullPath = Path.GetFullPath(profile);
		if (Path.GetPathRoot(fullPath)?.Equals(fullPath, StringComparison.OrdinalIgnoreCase) == true)
			throw new ArgumentException("profile 不能是文件系统根目录", nameof(profile));
		if (_diagnosticProfile is not null && !string.Equals(_diagnosticProfile, fullPath, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("profile 只能设置一次");
		_diagnosticProfile = fullPath;
	}

	/// <summary>
	/// SQLite 数据库文件路径
	/// </summary>
	public static string DatabasePath => Path.Combine(DataDir, DatabaseFileName);

	/// <summary>
	/// 资源根目录: &lt;data&gt;/resources
	/// </summary>
	public static string ResourcesDir => Path.Combine(DataDir, ResourcesDirName);

	/// <summary>
	/// 日志目录: &lt;data&gt;/log
	/// </summary>
	public static string LogDir => Path.Combine(DataDir, "log");

	/// <summary>ARG 知识库目录: &lt;data&gt;/knowledge</summary>
	public static string KnowledgeDir => Path.Combine(DataDir, "knowledge");

	/// <summary>运行时可编辑的 ARG 知识文件路径</summary>
	public static string MemoryMarkdownPath => Path.Combine(KnowledgeDir, "Memory.md");

	/// <summary>
	/// 创建数据目录与各子目录 (启动时调用, 幂等)
	/// </summary>
	public static void EnsureCreated()
	{
		Directory.CreateDirectory(DataDir);
		Directory.CreateDirectory(ResourcesDir);
		Directory.CreateDirectory(LogDir);
		Directory.CreateDirectory(KnowledgeDir);
	}

	/// <summary>
	/// 平台数据根目录
	///
	/// Environment.SpecialFolder.ApplicationData 在三个平台上分别是:
	/// Windows → %APPDATA%; macOS → ~/.config (与 Tauri 不同, 需要单独处理); Linux → ~/.local/share (XDG_DATA_HOME)
	/// </summary>
	private static string AppDataRoot()
	{
		if (OperatingSystem.IsMacOS())
		{
			// Tauri 在 macOS 上用的是 ~/Library/Application Support, 而 .NET 的 ApplicationData 指向 ~/.config
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			return Path.Combine(home, "Library", "Application Support");
		}
		return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
	}
}
