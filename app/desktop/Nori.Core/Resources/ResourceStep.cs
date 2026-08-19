namespace Nori.Core.Resources;

/// <summary>
/// 资源流程阶段
///
/// 与前端 services/resourceDownload.ts 的 ResourceStep 一一对应,
/// 阶段串必须保持不变: installed / downloading / download-done / extracting / done / error
/// </summary>
public sealed record ResourceStep
{
	/// <summary>阶段名</summary>
	public required string Step { get; init; }

	/// <summary>下载百分比</summary>
	public float? Progress { get; init; }

	/// <summary>已下载字节数</summary>
	public long? Downloaded { get; init; }

	/// <summary>文件总大小</summary>
	public long? Total { get; init; }

	/// <summary>错误信息</summary>
	public string? Message { get; init; }

	/// <summary>检测到已安装</summary>
	public static ResourceStep Installed() => new() {Step = "installed", Progress = 100f};

	/// <summary>下载中</summary>
	public static ResourceStep Downloading(DownloadProgress progress) => new()
	{
		Step = "downloading",
		Progress = progress.Percentage,
		Downloaded = progress.Downloaded,
		Total = progress.Total,
	};

	/// <summary>下载完成</summary>
	public static ResourceStep DownloadDone() => new() {Step = "download-done", Progress = 100f};

	/// <summary>解压中</summary>
	public static ResourceStep Extracting() => new() {Step = "extracting"};

	/// <summary>就绪</summary>
	public static ResourceStep Done() => new() {Step = "done", Progress = 100f};

	/// <summary>出错</summary>
	public static ResourceStep Error(string message) => new() {Step = "error", Message = message};
}
