using System.Text.Json;

namespace Nori.Core.Automation;

/// <summary>结构化自动化动作解析错误。</summary>
public sealed class AutomationActionValidationException : FormatException
{
	/// <summary>创建解析错误。</summary>
	public AutomationActionValidationException(string message) : base(message) { }
}

/// <summary>所有可执行的自动化动作基类。</summary>
public abstract record AutomationAction
{
	/// <summary>动作种类。</summary>
	public abstract AutomationActionKind Kind { get; }

	/// <summary>解析并按策略校验 JSON 动作。</summary>
	public static AutomationAction Parse(string json, AutomationPolicy policy)
	{
		if (!TryParse(json, policy, out AutomationAction? action, out string? error))
			throw new AutomationActionValidationException(error ?? "自动化动作无效");
		return action!;
	}

	/// <summary>尝试解析白名单动作，不记录输入正文。</summary>
	public static bool TryParse(string json, AutomationPolicy policy, out AutomationAction? action, out string? error)
	{
		ArgumentNullException.ThrowIfNull(policy);
		action = null;
		error = null;
		if (string.IsNullOrWhiteSpace(json)) { error = "自动化动作不能为空"; return false; }
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) throw new AutomationActionValidationException("自动化动作必须是 JSON 对象");
			string type = RequiredString(root, "type");
			action = type switch
			{
				"click" => new ClickAction(RequiredInt(root, "x"), RequiredInt(root, "y")),
				"type_text" => new TypeTextAction(RequiredString(root, "text")),
				"key_press" => new KeyPressAction(RequiredString(root, "key")),
				"scroll" => new ScrollAction(RequiredInt(root, "delta_x"), RequiredInt(root, "delta_y")),
				_ => throw new AutomationActionValidationException("自动化动作类型不在白名单内"),
			};
			if (!policy.TryValidate(action, out error)) { action = null; return false; }
			return true;
		}
		catch (AutomationActionValidationException exception) { error = exception.Message; return false; }
		catch (JsonException) { error = "自动化动作 JSON 无效"; return false; }
		catch (FormatException) { error = "自动化动作字段格式无效"; return false; }
		catch (OverflowException) { error = "自动化动作数值超出范围"; return false; }
	}

	private static string RequiredString(JsonElement root, string name)
	{
		if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
			throw new AutomationActionValidationException($"自动化动作缺少字段: {name}");
		string? text = value.GetString();
		if (string.IsNullOrEmpty(text)) throw new AutomationActionValidationException($"自动化动作字段不能为空: {name}");
		return text;
	}

	private static int RequiredInt(JsonElement root, string name)
	{
		if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
			throw new AutomationActionValidationException($"自动化动作缺少整数: {name}");
		return result;
	}
}

/// <summary>屏幕单击动作。</summary>
public sealed record ClickAction(int X, int Y) : AutomationAction
{
	/// <inheritdoc />
	public override AutomationActionKind Kind => AutomationActionKind.Click;
}

/// <summary>键盘输入动作；正文只在执行器生命周期内传递。</summary>
public sealed record TypeTextAction(string Text) : AutomationAction
{
	/// <inheritdoc />
	public override AutomationActionKind Kind => AutomationActionKind.TypeText;
}

/// <summary>受限键盘按键动作。</summary>
public sealed record KeyPressAction(string Key) : AutomationAction
{
	/// <inheritdoc />
	public override AutomationActionKind Kind => AutomationActionKind.KeyPress;
}

/// <summary>滚轮动作。</summary>
public sealed record ScrollAction(int DeltaX, int DeltaY) : AutomationAction
{
	/// <inheritdoc />
	public override AutomationActionKind Kind => AutomationActionKind.Scroll;
}
