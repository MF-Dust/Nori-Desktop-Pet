namespace Nori.Core.Automation;

/// <summary>桌面截图的尺寸与内存上限。</summary>
public static class AutomationCaptureLimits
{
	public const int MaxDimension = 4096;
	public const long MaxPixels = 12_582_912;
	public const long MaxRawBytes = MaxPixels * 4;
	public const int MaxEncodedBytes = 32 * 1024 * 1024;

	/// <summary>校验截图尺寸。</summary>
	public static bool TryValidate(int width, int height, out string? error)
	{
		if (width <= 0 || height <= 0) { error = "截图尺寸必须为正数"; return false; }
		if (width > MaxDimension || height > MaxDimension) { error = "截图单边尺寸超出限制"; return false; }
		if ((long)width * height > MaxPixels) { error = "截图像素数超出限制"; return false; }
		error = null;
		return true;
	}

	/// <summary>计算受限的 BGRA32 缓冲区大小。</summary>
	public static bool TryGetRawByteCount(int width, int height, out int byteCount, out string? error)
	{
		byteCount = 0;
		if (!TryValidate(width, height, out error)) return false;
		long bytes = (long)width * height * 4;
		if (bytes > int.MaxValue) { error = "截图原始内存超出限制"; return false; }
		byteCount = (int)bytes;
		return true;
	}
}
