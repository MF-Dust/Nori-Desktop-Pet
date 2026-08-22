using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Nori.Desktop.Settings;

/// <summary>设置页 Fluent 主题资源访问。</summary>
internal static class SettingsTheme
{
	private static readonly IReadOnlyDictionary<string, string> ResourceKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["#E8F6FF"] = "TextFillColorPrimaryBrush",
		["#D8F4FF"] = "TextFillColorPrimaryBrush",
		["#C5D8E3"] = "TextFillColorSecondaryBrush",
		["#B7D8E8"] = "TextFillColorSecondaryBrush",
		["#A9C0CE"] = "TextFillColorSecondaryBrush",
		["#8CA6B8"] = "TextFillColorSecondaryBrush",
		["#718C9E"] = "TextFillColorSecondaryBrush",
		["#7DE3FF"] = "AccentFillColorDefaultBrush",
		["#63D8C1"] = "AccentButtonBackground",
		["#062029"] = "AccentButtonForeground",
		["#31566A"] = "ControlStrokeColorDefaultBrush",
		["#1D4053"] = "ControlStrokeColorDefaultBrush",
		["#0D2232"] = "ControlFillColorDefaultBrush",
		["#102736"] = "ControlAltFillColorSecondaryBrush",
		["#173A4C"] = "ControlFillColorDefaultBrush",
		["#FF9BA7"] = "SystemFillColorCriticalBrush",
		["#FFD895"] = "SystemFillColorCautionBrush",
		["#3D2630"] = "SystemFillColorCriticalBackgroundBrush",
		["#3B3020"] = "SystemFillColorCautionBackgroundBrush",
		["#9A5564"] = "SystemFillColorCriticalBrush",
		["#8A6936"] = "SystemFillColorCautionBrush",
		["#522B38"] = "SystemFillColorCriticalBackgroundBrush",
		["#A85A68"] = "SystemFillColorCriticalBrush",
		["#0B2030"] = "SolidBackgroundFillColorSecondaryBrush",
		["#081724"] = "SolidBackgroundFillColorBaseBrush",
	};

	public static IBrush FromLegacy(string fallback)
	{
		string? resourceKey = ResourceKeys.TryGetValue(fallback, out string? key) ? key : null;
		if (resourceKey is not null && TryFindResource(resourceKey, out object? resource) && resource is IBrush brush)
		{
			return brush;
		}
		return new SolidColorBrush(Color.Parse(fallback));
	}

	public static IBrush Resource(string resourceKey, string fallback) =>
		TryFindResource(resourceKey, out object? resource) && resource is IBrush brush
			? brush
			: new SolidColorBrush(Color.Parse(fallback));

	private static bool TryFindResource(string key, out object? resource)
	{
		resource = null;
		return Application.Current is IResourceHost host && host.TryFindResource(key, out resource);
	}
}
