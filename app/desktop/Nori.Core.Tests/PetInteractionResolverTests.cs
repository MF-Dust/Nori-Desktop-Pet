using Nori.Core.Live2D;

namespace Nori.Core.Tests;

public sealed class PetInteractionResolverTests
{
	[Fact]
	public void OverlapChoosesSmallestArea()
	{
		PetInteractionConfig config = new()
		{
			Regions =
			[
				Region("body", "身体", 0, 0, 0.8, 0.8),
				Region("face", "脸", 0.2, 0.2, 0.2, 0.2),
			],
		};

		Assert.True(PetInteractionResolver.TryResolve(config, 0.3, 0.3, out PetInteractionHit? hit));
		Assert.NotNull(hit);
		Assert.Equal("face", hit!.Region.Id);
		Assert.Equal(0.5, hit.RegionX, 10);
		Assert.Equal(0.5, hit.RegionY, 10);
	}

	[Fact]
	public void EqualAreaKeepsConfigurationOrder()
	{
		PetInteractionConfig config = new()
		{
			Regions =
			[
				Region("first", "第一", 0, 0, 0.4, 0.4),
				Region("second", "第二", 0.2, 0.2, 0.4, 0.4),
			],
		};

		Assert.True(PetInteractionResolver.TryResolve(config, 0.3, 0.3, out PetInteractionHit? hit));
		Assert.Equal("first", hit!.Region.Id);
	}

	[Theory]
	[InlineData(-0.01, 0.5)]
	[InlineData(0.5, 1.01)]
	public void OutsideModelDoesNotHit(double x, double y)
	{
		PetInteractionConfig config = new()
		{
			Regions = [Region("body", "身体", 0, 0, 1, 1)],
		};

		Assert.False(PetInteractionResolver.TryResolve(config, x, y, out _));
	}

	private static PetInteractionRegion Region(string id, string name, double x, double y, double width, double height) => new()
	{
		Id = id,
		Name = name,
		Rect = new PetInteractionRect {X = x, Y = y, Width = width, Height = height},
		Motion = PetInteractionAction.None,
		Expression = PetInteractionAction.None,
	};
}
