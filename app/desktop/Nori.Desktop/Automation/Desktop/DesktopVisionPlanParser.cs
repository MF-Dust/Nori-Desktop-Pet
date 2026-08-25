using System.Text.Json;
using Nori.Core.Automation;

namespace Nori.Desktop.Automation.Desktop;

/// <summary>严格解析视觉规划器的单动作或完成结果。</summary>
internal static class DesktopVisionPlanParser
{
	public static bool TryParse(
		string? raw,
		int maxCharacters,
		out bool completed,
		out AutomationAction? action)
	{
		completed = false;
		action = null;
		if (string.IsNullOrWhiteSpace(raw) || raw.Length > maxCharacters) return false;

		try
		{
			using JsonDocument document = JsonDocument.Parse(raw);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) return false;

			if (HasExactProperties(root, "status") && TryGetString(root, "status", out string status))
			{
				completed = status == "completed";
				return completed;
			}

			if (!TryGetString(root, "type", out string type)) return false;
			switch (type)
			{
				case "click":
					if (!HasExactProperties(root, "type", "x", "y")
						|| !TryGetInt32(root, "x", out int clickX)
						|| !TryGetInt32(root, "y", out int clickY)) return false;
					action = new ClickAction(clickX, clickY);
					return true;
				case "type_text":
					if (!HasExactProperties(root, "type", "text")
						|| !TryGetString(root, "text", out string text)) return false;
					action = new TypeTextAction(text);
					return true;
				case "key_press":
					if (!HasExactProperties(root, "type", "key")
						|| !TryGetString(root, "key", out string key)) return false;
					action = new KeyPressAction(key);
					return true;
				case "scroll":
					if (!HasExactProperties(root, "type", "delta_x", "delta_y")
						|| !TryGetInt32(root, "delta_x", out int deltaX)
						|| !TryGetInt32(root, "delta_y", out int deltaY)) return false;
					action = new ScrollAction(deltaX, deltaY);
					return true;
				default:
					return false;
			}
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static bool HasExactProperties(JsonElement root, params string[] names)
	{
		HashSet<string> expected = new(names, StringComparer.Ordinal);
		HashSet<string> actual = new(StringComparer.Ordinal);
		foreach (JsonProperty property in root.EnumerateObject())
		{
			if (!expected.Contains(property.Name) || !actual.Add(property.Name)) return false;
		}
		return actual.Count == expected.Count;
	}

	private static bool TryGetString(JsonElement root, string name, out string value)
	{
		if (!root.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.String)
		{
			value = string.Empty;
			return false;
		}
		value = element.GetString() ?? string.Empty;
		return true;
	}

	private static bool TryGetInt32(JsonElement root, string name, out int value)
	{
		if (!root.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.Number)
		{
			value = default;
			return false;
		}
		return element.TryGetInt32(out value);
	}
}
