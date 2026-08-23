using Nori.Core.Live2D;

namespace Nori.Core.Tests;

public sealed class PetInteractionConfigTests
{
	[Fact]
	public void EmptyConfigHasVersionOneAndNoRegions()
	{
		PetInteractionConfig config = PetInteractionConfig.Empty;

		config.Validate();
		Assert.Equal(1, config.Version);
		Assert.Empty(config.Regions);
	}

	[Fact]
	public void JsonRoundTripUsesFrontendContractNames()
	{
		PetInteractionConfig original = new()
		{
			Regions =
			[
				new PetInteractionRegion
				{
					Id = "head",
					Name = "头部",
					ReactionMode = PetInteractionReactionMode.Ai,
					Rect = new PetInteractionRect {X = 0.2, Y = 0.1, Width = 0.4, Height = 0.3},
					Motion = new PetInteractionAction
					{
						Mode = PetInteractionActionMode.Selected,
						Group = "Reactions",
						Name = "01_Nod",
					},
					Expression = new PetInteractionAction {Mode = PetInteractionActionMode.Random},
				},
			],
		};

		string json = original.ToJsonNode().ToJsonString();
		PetInteractionConfig parsed = PetInteractionConfig.Parse(json);

		Assert.Contains("reactionMode", json);
		Assert.Contains("selected", json);
		Assert.Equal(original.Version, parsed.Version);
		PetInteractionRegion region = Assert.Single(parsed.Regions);
		Assert.Equal("head", region.Id);
		Assert.Equal("头部", region.Name);
		Assert.Equal(PetInteractionReactionMode.Ai, region.ReactionMode);
		Assert.Equal("Reactions", region.Motion.Group);
		Assert.Equal("01_Nod", region.Motion.Name);
		Assert.Equal(PetInteractionActionMode.Random, region.Expression.Mode);
	}

	[Theory]
	[InlineData(0, 0, 0, 0)]
	[InlineData(-0.1, 0, 0.2, 0.2)]
	[InlineData(0.9, 0, 0.2, 0.2)]
	[InlineData(0, 0, 1.01, 0.2)]
	public void InvalidRectIsRejected(double x, double y, double width, double height)
	{
		PetInteractionConfig config = ConfigWith(new PetInteractionRect {X = x, Y = y, Width = width, Height = height});

		Assert.Throws<InvalidOperationException>(() => config.Validate());
	}

	[Fact]
	public void DuplicateRegionIdIsRejected()
	{
		PetInteractionConfig config = new()
		{
			Regions =
			[
				Region("same", "头部", new PetInteractionRect {X = 0, Y = 0, Width = 0.2, Height = 0.2}),
				Region("same", "脸", new PetInteractionRect {X = 0.3, Y = 0, Width = 0.2, Height = 0.2}),
			],
		};

		Assert.Throws<InvalidOperationException>(() => config.Validate());
	}

	[Fact]
	public void SelectedBindingsMustExistInCurrentModel()
	{
		PetInteractionConfig config = ConfigWith(new PetInteractionRect {X = 0, Y = 0, Width = 0.2, Height = 0.2}, new PetInteractionAction
		{
			Mode = PetInteractionActionMode.Selected,
			Group = "Reactions",
			Name = "01_Nod",
		});

		Assert.Throws<InvalidOperationException>(() => config.ValidateBindings(
			[new MotionGroupInfo {Group = "Reactions", Names = ["02_Shake"]}],
			["01_Smile"]));
	}

	private static PetInteractionConfig ConfigWith(PetInteractionRect rect, PetInteractionAction? motion = null) => new()
	{
		Regions = [Region("region", "区域", rect, motion)],
	};

	private static PetInteractionRegion Region(string id, string name, PetInteractionRect rect, PetInteractionAction? motion = null) => new()
	{
		Id = id,
		Name = name,
		Rect = rect,
		Motion = motion ?? PetInteractionAction.None,
		Expression = PetInteractionAction.None,
	};
}
