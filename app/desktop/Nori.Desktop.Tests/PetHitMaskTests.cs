using Nori.Desktop.Live2D;

namespace Nori.Desktop.Tests;

public sealed class PetHitMaskTests
{
	[Fact]
	public void SourcePixelsKeepTopAndBottomOrientation()
	{
		byte[] bits = new byte[PetHitMask.ByteLength];
		byte[] topPixels = CreatePixels(PetHitMask.Width, PetHitMask.Height);
		SetAlphaAtTop(topPixels, PetHitMask.Width, PetHitMask.Height, 48, 0);

		PetHitMask.BuildFromSourcePixels(topPixels, PetHitMask.Width, PetHitMask.Height, bits);

		Assert.True(PetHitMask.IsPointOnModel(bits, 48.5, 0.5, PetHitMask.Width, PetHitMask.Height));
		Assert.False(PetHitMask.IsPointOnModel(bits, 48.5, PetHitMask.Height - 0.5, PetHitMask.Width, PetHitMask.Height));

		byte[] bottomPixels = CreatePixels(PetHitMask.Width, PetHitMask.Height);
		SetAlphaAtTop(bottomPixels, PetHitMask.Width, PetHitMask.Height, 48, PetHitMask.Height - 1);
		PetHitMask.BuildFromSourcePixels(bottomPixels, PetHitMask.Width, PetHitMask.Height, bits);

		Assert.False(PetHitMask.IsPointOnModel(bits, 48.5, 0.5, PetHitMask.Width, PetHitMask.Height));
		Assert.True(PetHitMask.IsPointOnModel(bits, 48.5, PetHitMask.Height - 0.5, PetHitMask.Width, PetHitMask.Height));
	}

	[Fact]
	public void ReducedPixelsKeepTopAndBottomOrientation()
	{
		byte[] bits = new byte[PetHitMask.ByteLength];
		byte[] topPixels = CreatePixels(PetHitMask.Width, PetHitMask.Height);
		SetAlphaAtTop(topPixels, PetHitMask.Width, PetHitMask.Height, 48, 0);

		PetHitMask.BuildFromReducedPixels(topPixels, PetHitMask.Width, PetHitMask.Height, bits);

		Assert.True(PetHitMask.IsPointOnModel(bits, 48.5, 0.5, PetHitMask.Width, PetHitMask.Height));
		Assert.False(PetHitMask.IsPointOnModel(bits, 48.5, PetHitMask.Height - 0.5, PetHitMask.Width, PetHitMask.Height));

		byte[] bottomPixels = CreatePixels(PetHitMask.Width, PetHitMask.Height);
		SetAlphaAtTop(bottomPixels, PetHitMask.Width, PetHitMask.Height, 48, PetHitMask.Height - 1);
		PetHitMask.BuildFromReducedPixels(bottomPixels, PetHitMask.Width, PetHitMask.Height, bits);

		Assert.False(PetHitMask.IsPointOnModel(bits, 48.5, 0.5, PetHitMask.Width, PetHitMask.Height));
		Assert.True(PetHitMask.IsPointOnModel(bits, 48.5, PetHitMask.Height - 0.5, PetHitMask.Width, PetHitMask.Height));
	}

	[Fact]
	public void SourcePixelsDetectSamplesAtEachPointInsideCell()
	{
		foreach ((double xFraction, double yFraction) in new[]
		{
			(0.2, 0.2),
			(0.8, 0.2),
			(0.2, 0.8),
			(0.8, 0.8),
			(0.5, 0.5),
		})
		{
			const int sourceWidth = 960;
			const int sourceHeight = 1280;
			const int column = 17;
			const int row = 29;
			byte[] pixels = CreatePixels(sourceWidth, sourceHeight);
			SetAlphaAtCellSample(pixels, sourceWidth, sourceHeight, column, row, xFraction, yFraction);
			byte[] bits = new byte[PetHitMask.ByteLength];

			PetHitMask.BuildFromSourcePixels(pixels, sourceWidth, sourceHeight, bits);

			Assert.True(PetHitMask.IsPointOnModel(bits, column + 0.5, row + 0.5, PetHitMask.Width, PetHitMask.Height));
		}
	}

	[Fact]
	public void SourcePixelsDoNotDilateIntoNeighboringCells()
	{
		const int sourceWidth = 960;
		const int sourceHeight = 1280;
		const int column = 10;
		const int row = 20;
		byte[] pixels = CreatePixels(sourceWidth, sourceHeight);
		SetAlphaAtCellSample(pixels, sourceWidth, sourceHeight, column, row, 0.5, 0.5);
		byte[] bits = new byte[PetHitMask.ByteLength];

		PetHitMask.BuildFromSourcePixels(pixels, sourceWidth, sourceHeight, bits);

		Assert.True(PetHitMask.IsPointOnModel(bits, column + 0.5, row + 0.5, PetHitMask.Width, PetHitMask.Height));
		Assert.False(PetHitMask.IsPointOnModel(bits, column - 0.5, row + 0.5, PetHitMask.Width, PetHitMask.Height));
		Assert.False(PetHitMask.IsPointOnModel(bits, column + 1.5, row + 0.5, PetHitMask.Width, PetHitMask.Height));
		Assert.False(PetHitMask.IsPointOnModel(bits, column + 0.5, row - 0.5, PetHitMask.Width, PetHitMask.Height));
		Assert.False(PetHitMask.IsPointOnModel(bits, column + 0.5, row + 1.5, PetHitMask.Width, PetHitMask.Height));
	}

	[Fact]
	public void DistantVisiblePixelsProduceOneContinuousModelRectangle()
	{
		byte[] pixels = CreatePixels(PetHitMask.Width, PetHitMask.Height);
		SetAlphaAtCellSample(pixels, PetHitMask.Width, PetHitMask.Height, 3, 2, 0.5, 0.5);
		SetAlphaAtCellSample(pixels, PetHitMask.Width, PetHitMask.Height, 7, 5, 0.5, 0.5);
		byte[] bits = new byte[PetHitMask.ByteLength];
		PetHitMask.BuildFromSourcePixels(pixels, PetHitMask.Width, PetHitMask.Height, bits);

		Assert.True(PetHitMask.IsPointOnModel(bits, 5.5, 3.5, PetHitMask.Width, PetHitMask.Height));
		Assert.False(PetHitMask.IsPointOnModel(bits, 2.5, 3.5, PetHitMask.Width, PetHitMask.Height));
		Assert.False(PetHitMask.IsPointOnModel(bits, 8.5, 3.5, PetHitMask.Width, PetHitMask.Height));

		List<(int X, int Y, int Width, int Height)> regions = PetHitMask.BuildHitRegions(bits, 192, 256);

		Assert.Single(regions);
		Assert.Equal((6, 4, 10, 8), regions[0]);
	}

	[Fact]
	public void InvalidOrEmptyBuffersProduceNoHits()
	{
		byte[] bits = Enumerable.Repeat(byte.MaxValue, PetHitMask.ByteLength).ToArray();

		PetHitMask.BuildFromSourcePixels([], 0, 0, bits);

		Assert.False(PetHitMask.IsPointOnModel(bits, 1, 1, 100, 100));
		Assert.Empty(PetHitMask.BuildHitRegions(bits, 100, 100));
		Assert.Empty(PetHitMask.BuildHitRegions(bits, 0, 100));
	}

	private static byte[] CreatePixels(int width, int height) => new byte[checked(width * height * 4)];

	private static void SetAlphaAtTop(byte[] pixels, int width, int height, int x, int topY, byte alpha = byte.MaxValue)
	{
		int glY = height - 1 - topY;
		pixels[(glY * width + x) * 4 + 3] = alpha;
	}

	private static void SetAlphaAtCellSample(
		byte[] pixels,
		int width,
		int height,
		int column,
		int row,
		double xFraction,
		double yFraction,
		byte alpha = byte.MaxValue)
	{
		int x = Math.Min(width - 1, Math.Max(0, (int)Math.Floor((column + xFraction) * width / PetHitMask.Width)));
		int topY = Math.Min(height - 1, Math.Max(0, (int)Math.Floor((row + yFraction) * height / PetHitMask.Height)));
		SetAlphaAtTop(pixels, width, height, x, topY, alpha);
	}
}
