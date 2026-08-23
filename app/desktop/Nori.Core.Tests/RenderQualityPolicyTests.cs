using Nori.Core.Live2D;

namespace Nori.Core.Tests;

public sealed class RenderQualityPolicyTests
{
	[Fact]
	public void AdaptiveStartsAtQualityTargetOnAc()
	{
		RenderQualityPolicy policy = new(
			Live2DRenderSettings.Normalize("arg-nori"),
			Live2DPowerSource.Ac);

		Assert.Equal("quality", policy.Current.EffectiveQuality);
		Assert.Equal(60, policy.Current.EffectiveFps);
		Assert.Equal(2.0f, policy.Current.EffectiveRenderScale);
	}

	[Fact]
	public void AdaptiveStartsAtBatteryTarget()
	{
		RenderQualityPolicy policy = new(
			Live2DRenderSettings.Normalize("nori"),
			Live2DPowerSource.Battery);

		Assert.Equal("balanced", policy.Current.EffectiveQuality);
		Assert.Equal(30, policy.Current.EffectiveFps);
		Assert.Equal(1.0f, policy.Current.EffectiveRenderScale);
	}

	[Fact]
	public void SustainedOverBudgetDegradesAdaptiveQuality()
	{
		RenderQualityPolicy policy = new(
			Live2DRenderSettings.Normalize("arg-nori"),
			Live2DPowerSource.Ac);

		for (int i = 0; i < 105; i++) policy.ObserveFrame(20);

		Assert.Equal("balanced", policy.Current.EffectiveQuality);
		Assert.Equal(45, policy.Current.EffectiveFps);
		Assert.True(policy.Current.IsDegraded);
	}

	[Fact]
	public void StableFramesRecoverAdaptiveQuality()
	{
		RenderQualityPolicy policy = new(
			Live2DRenderSettings.Normalize("arg-nori"),
			Live2DPowerSource.Ac);
		for (int i = 0; i < 105; i++) policy.ObserveFrame(20);

		for (int i = 0; i < 501; i++) policy.ObserveFrame(10);

		Assert.Equal("quality", policy.Current.EffectiveQuality);
		Assert.Equal(60, policy.Current.EffectiveFps);
		Assert.False(policy.Current.IsDegraded);
	}

	[Fact]
	public void ExplicitFpsCapsEveryQualityMode()
	{
		RenderQualityPolicy policy = new(
			Live2DRenderSettings.Normalize("nori", maxFps: 15),
			Live2DPowerSource.Ac);

		Assert.Equal(15, policy.Current.EffectiveFps);
		for (int i = 0; i < 200; i++) policy.ObserveFrame(20);
		Assert.Equal(15, policy.Current.EffectiveFps);
	}

	[Fact]
	public void FixedQualityModesDoNotAdapt()
	{
		RenderQualityPolicy policy = new(
			Live2DRenderSettings.Normalize("arg-nori", qualityMode: "quality"),
			Live2DPowerSource.Battery);

		for (int i = 0; i < 1000; i++) policy.ObserveFrame(100);

		Assert.Equal("quality", policy.Current.EffectiveQuality);
		Assert.Equal(45, policy.Current.EffectiveFps);
	}
}
