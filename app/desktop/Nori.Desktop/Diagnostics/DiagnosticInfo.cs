using System.Reflection;
using System.Runtime.InteropServices;
using Nori.Core.Data;

namespace Nori.Desktop.Diagnostics;

/// <summary>
/// 运行诊断信息构建器
///
/// 迷你版 ClassIsland DiagnosticService.GetDiagnosticInfo:
/// 汇总调试页展示与问题反馈所需的静态环境信息, 全部为即时读取, 无需持有状态.
/// </summary>
public static class DiagnosticInfo
{
	/// <summary>
	/// 构建诊断信息字典 (键为英文 snake_case 便于检索, 值为可读文本)
	/// </summary>
	public static Dictionary<string, string> Build()
	{
		string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
		long dbBytes = FileSizeOrDefault(AppPaths.DatabasePath);
		return new Dictionary<string, string>
		{
			["app_version"] = version,
			["dotnet_version"] = Environment.Version.ToString(),
			["os_version"] = RuntimeInformation.OSDescription,
			["os_arch"] = RuntimeInformation.OSArchitecture.ToString(),
			["process_uptime"] = FormatDuration(Environment.TickCount64),
			["process_bits"] = Environment.Is64BitProcess ? "64-bit" : "32-bit",
			["data_dir"] = AppPaths.DataDir,
			["resources_dir"] = AppPaths.ResourcesDir,
			["log_dir"] = AppPaths.LogDir,
			["database_path"] = AppPaths.DatabasePath,
			["database_size"] = dbBytes < 0 ? "不存在" : FormatSize(dbBytes),
		};
	}

	/// <summary>
	/// 把字典拼成可复制的多行文本 (键: 值)
	/// </summary>
	public static string ToText(Dictionary<string, string> info) =>
		string.Join(Environment.NewLine, info.Select(pair => $"{pair.Key}: {pair.Value}"));

	/// <summary>
	/// 取文件大小; 文件不存在返回 -1, 读取失败按 0 处理 (诊断信息不能反过来抛异常)
	/// </summary>
	private static long FileSizeOrDefault(string path)
	{
		try
		{
			FileInfo file = new(path);
			return file.Exists ? file.Length : -1;
		}
		catch (IOException)
		{
			return 0;
		}
		catch (UnauthorizedAccessException)
		{
			return 0;
		}
	}

	private static string FormatDuration(long milliseconds)
	{
		TimeSpan span = TimeSpan.FromMilliseconds(milliseconds);
		return span.Days > 0 ? $"{span.Days}天{span.Hours}小时{span.Minutes}分" : $"{span.Hours}小时{span.Minutes}分{span.Seconds}秒";
	}

	private static string FormatSize(long bytes) => bytes switch
	{
		>= 1L << 30 => $"{bytes >> 30} GB",
		>= 1L << 20 => $"{bytes >> 20} MB",
		>= 1L << 10 => $"{bytes >> 10} KB",
		_ => $"{bytes} B",
	};
}
