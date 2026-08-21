using Nori.Core.Live2D;

namespace Nori.Core.Tests;

public class MotionSelectorTests
{
	[Fact]
	public void TapBody_HasHighestPriority_AndPreservesOriginalName()
	{
		var candidates = MotionSelector.GetInteractionCandidates([
			Group("Idle", "idle"),
			Group("TAP-BODY", "tap-1", "tap-2"),
			Group("Reactions", "nod"),
		]);

		Assert.Collection(candidates,
			group => Assert.Equal("TAP-BODY", group.Group),
			group => Assert.Equal("Reactions", group.Group));
	}

	[Fact]
	public void SemanticGroups_AreOrderedBeforeOtherNonIdleGroups()
	{
		var candidates = MotionSelector.GetInteractionCandidates([
			Group("Effects", "glitch"),
			Group("Actions", "wave"),
			Group("Reactions", "nod"),
			Group("Touch", "pat"),
		]);

		Assert.Equal(["Touch", "Reactions", "Actions", "Effects"], candidates.Select(group => group.Group));
	}

	[Fact]
	public void GenericNonIdleGroup_IsUsedWhenNoSemanticGroupExists()
	{
		var candidates = MotionSelector.GetInteractionCandidates([
			Group("Background", "back"),
			Group("Idle", "sleep"),
			Group("Dance", "dance-1"),
		]);

		var candidate = Assert.Single(candidates);
		Assert.Equal("Dance", candidate.Group);
	}

	[Fact]
	public void EmptyGroups_AreIgnored()
	{
		var candidates = MotionSelector.GetInteractionCandidates([
			Group("Reactions"),
			Group("TapBody", "tap"),
			Group("", "unnamed"),
		]);

		var candidate = Assert.Single(candidates);
		Assert.Equal("TapBody", candidate.Group);
	}

	[Fact]
	public void OnlyIdleOrBackgroundGroups_HasNoInteractionCandidate()
	{
		var candidates = MotionSelector.GetInteractionCandidates([
			Group("IdleLoop", "idle"),
			Group("background_layer", "background"),
		]);

		Assert.Empty(candidates);
	}

	private static MotionGroupInfo Group(string group, params string[] names) => new()
	{
		Group = group,
		Names = [.. names],
	};
}
