namespace Nori.Core.Data;

/// <summary>
/// 旧 Tauri 数据布局的兼容常量。
///
/// 新宿主不从这里读取或写入路径；业务路径必须显式使用 <see cref="AppStoragePaths" />。
/// 旧目录的解析只允许出现在 <see cref="LegacyDataPathResolver" /> 的迁移入口。
/// </summary>
public static class AppPaths
{
	/// <summary>旧应用标识，与 tauri.conf.json identifier 保持一致。</summary>
	public const string Identifier = "cn.erhio.noriDesktopPet";

	/// <summary>数据库文件名。</summary>
	public const string DatabaseFileName = "nori.db";

	/// <summary>旧资源目录名。</summary>
	public const string ResourcesDirName = "resources";
}
