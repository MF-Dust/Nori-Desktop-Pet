namespace Nori.Core.Resources;

/// <summary>
/// ZIP 解压资源上限.
///
/// 上限同时作用于目录项数量、单个文件展开大小、所有文件展开总量与压缩比,
/// 用来阻止 ZIP bomb 在 staging 阶段耗尽磁盘或内存.
/// </summary>
public sealed record ZipExtractionLimits
{
	/// <summary>压缩包允许包含的最大条目数 (目录项也计入).</summary>
	public int MaxEntryCount { get; init; } = 4096;

	/// <summary>单个文件允许展开的最大字节数.</summary>
	public long MaxSingleFileBytes { get; init; } = 256L * 1024 * 1024;

	/// <summary>所有文件允许展开的最大总字节数.</summary>
	public long MaxTotalUncompressedBytes { get; init; } = 512L * 1024 * 1024;

	/// <summary>允许的最大展开字节数 / 压缩字节数.</summary>
	public double MaxCompressionRatio { get; init; } = 200.0;

	internal void Validate()
	{
		if (MaxEntryCount <= 0) throw new ArgumentOutOfRangeException(nameof(MaxEntryCount));
		if (MaxSingleFileBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxSingleFileBytes));
		if (MaxTotalUncompressedBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxTotalUncompressedBytes));
		if (double.IsNaN(MaxCompressionRatio) || double.IsInfinity(MaxCompressionRatio) || MaxCompressionRatio <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(MaxCompressionRatio));
		}
	}
}
