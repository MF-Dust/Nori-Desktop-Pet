using Nori.Core.Live2D;

namespace Nori.Core.Tests;

public sealed class PetActionResolverTests
{
	private static readonly IReadOnlyList<MotionGroupInfo> Motions =
	[
		new MotionGroupInfo {Group = "Reactions", Names = ["01_Idle_Loop", "02_Nod", "04_WakuWaku", "sleep_Loop"]},
	];

	[Theory]
	[InlineData("nod", "02_Nod")]
	[InlineData("excited", "04_WakuWaku")]
	[InlineData("sleep", "sleep_Loop")]
	[InlineData("01_Idle_Loop", "01_Idle_Loop")]
	public void NaturalMotionNamesResolve(string requested, string expected) =>
		Assert.Equal(expected, PetActionResolver.ResolveMotion(Motions, requested));

	[Theory]
	[InlineData("happy", "13_Happy")]
	[InlineData("smile", "07_Smile")]
	[InlineData("surprised", "14_Surprised")]
	[InlineData("03_Angry", "03_Angry")]
	public void NaturalExpressionNamesResolve(string requested, string expected) =>
		Assert.Equal(expected, PetActionResolver.ResolveExpression(
			["00_Default", "03_Angry", "07_Smile", "13_Happy", "14_Surprised"], requested));

	[Fact]
	public void UnknownNameReturnsNull() =>
		Assert.Null(PetActionResolver.ResolveMotion(Motions, "wave"));
}
