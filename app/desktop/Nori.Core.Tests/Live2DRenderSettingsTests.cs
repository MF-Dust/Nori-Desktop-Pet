using Nori.Core.Live2D;

namespace Nori.Core.Tests;

public sealed class Live2DRenderSettingsTests
{
	[Fact]
	public void InvalidValuesAreNormalizedToSafeNativeRanges()
	{
		Live2DRenderSettings settings = Live2DRenderSettings.Normalize(
			" arg-nori ",
			opacity: 4,
			renderScale: 0.1f,
			qualityMode: "not-a-mode",
			maxFps: 999);

		Assert.Equal("arg-nori", settings.ModelId);
		Assert.Equal(1.0f, settings.Opacity);
		Assert.Equal(0.5f, settings.RenderScale);
		Assert.Equal(Live2DQualityMode.Adaptive, settings.QualityMode);
		Assert.Equal(240, settings.MaxFps);
	}

	[Fact]
	public void NativeSliceContainsOnlyTheTwoExistingModelIds()
	{
		Assert.True(Live2DModelCatalog.IsNativeModel("arg-nori"));
		Assert.True(Live2DModelCatalog.IsNativeModel("NORI"));
		Assert.False(Live2DModelCatalog.IsNativeModel("imported-model"));
	}

	[Fact]
	public void QualityModeStorageIsCanonical()
	{
		Assert.Equal("adaptive", Live2DRenderSettings.QualityModeToStorage(Live2DQualityMode.Adaptive));
		Assert.Equal("quality", Live2DRenderSettings.QualityModeToStorage(Live2DQualityMode.Quality));
		Assert.Equal("eco", Live2DRenderSettings.QualityModeToStorage(Live2DQualityMode.Eco));
	}
}
