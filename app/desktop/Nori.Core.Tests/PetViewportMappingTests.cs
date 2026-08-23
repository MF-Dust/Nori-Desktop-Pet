using Nori.Core.Live2D;

namespace Nori.Core.Tests;

public sealed class PetViewportMappingTests
{
	[Fact]
	public void TallModelFillsSquareViewport()
	{
		// CubismModelMatrix 使用 Unit 尺寸：1×2 画布 SetHeight(2) 后 scale=1。
		PetViewportMapping mapping = PetViewportMapping.Create(
			500, 500, 1, 2, 1, 1);

		PetViewportRect rect = mapping.ModelRect;

		Assert.Equal(125, rect.Left, 10);
		Assert.Equal(0, rect.Top, 10);
		Assert.Equal(250, rect.Width, 10);
		Assert.Equal(500, rect.Height, 10);
	}

	[Fact]
	public void WideModelIsCenteredWithLetterbox()
	{
		// 2×1 Unit 画布 SetHeight(2) 后 scale=2，宽模型再由投影缩小一半。
		PetViewportMapping mapping = PetViewportMapping.Create(
			500, 500, 2, 1, 2, 2);

		PetViewportRect rect = mapping.ModelRect;

		Assert.Equal(0, rect.Left, 10);
		Assert.Equal(125, rect.Top, 10);
		Assert.Equal(500, rect.Width, 10);
		Assert.Equal(250, rect.Height, 10);
	}

	[Fact]
	public void ClientCoordinatesRoundTripToNormalizedCanvas()
	{
		PetViewportMapping mapping = PetViewportMapping.Create(
			500, 500, 1, 2, 1, 1);

		Assert.True(mapping.TryMapClientToModel(187.5, 250, out double x, out double y));
		Assert.Equal(0.25, x, 10);
		Assert.Equal(0.5, y, 10);
	}

	[Fact]
	public void LetterboxIsOutsideModelCanvas()
	{
		PetViewportMapping mapping = PetViewportMapping.Create(
			500, 500, 2, 1, 2, 2);

		Assert.False(mapping.TryMapClientToModel(250, 50, out _, out _));
		Assert.True(mapping.TryMapClientToModel(250, 250, out double x, out double y));
		Assert.Equal(0.5, x, 10);
		Assert.Equal(0.5, y, 10);
	}
}
