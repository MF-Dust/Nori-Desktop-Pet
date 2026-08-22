using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace Nori.Desktop.Settings;

/// <summary>设置页统一的 Fluent 确认与表单对话框。</summary>
public static class SettingsDialogs
{
	public sealed record FormField(string Key, string Label, string Value = "", bool Multiline = false, bool Password = false);

	public static async Task<bool> ConfirmAsync(Window owner, string title, string message, bool danger = false)
	{
		StackPanel content = new() {Spacing = 12, MaxWidth = 520};
		if (danger)
		{
			content.Children.Add(new FAInfoBar
			{
				IsOpen = true,
				Severity = FAInfoBarSeverity.Error,
				Message = message,
				IsClosable = false,
			});
		}
		else
		{
			content.Children.Add(new TextBlock
			{
				Text = message,
				TextWrapping = TextWrapping.Wrap,
			});
		}
		FAContentDialog dialog = new()
		{
			Title = title,
			Content = content,
			PrimaryButtonText = danger ? SettingsLocalization.Text("common.confirm") : SettingsLocalization.Text("common.save"),
			CloseButtonText = SettingsLocalization.Text("common.cancel"),
			DefaultButton = FAContentDialogButton.Primary,
		};
		FAContentDialogResult result = await dialog.ShowAsync(owner);
		return result == FAContentDialogResult.Primary;
	}

	public static async Task<IReadOnlyDictionary<string, string>?> FormAsync(Window owner, string title, IReadOnlyList<FormField> fields, string confirmText)
	{
		Dictionary<string, TextBox> inputs = [];
		StackPanel form = new() {Spacing = 10, Width = 520};
		foreach (FormField field in fields)
		{
			StackPanel row = new() {Spacing = 4};
			row.Children.Add(new TextBlock {Text = field.Label});
			TextBox input = new()
			{
				Text = field.Value,
				AcceptsReturn = field.Multiline,
				TextWrapping = field.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
				MinHeight = field.Multiline ? 100 : 32,
				VerticalContentAlignment = field.Multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
			};
			if (field.Password) input.PasswordChar = '•';
			inputs[field.Key] = input;
			row.Children.Add(input);
			form.Children.Add(row);
		}
		ScrollViewer scroll = new() {MaxHeight = 520, Content = form};
		FAContentDialog dialog = new()
		{
			Title = title,
			Content = scroll,
			PrimaryButtonText = confirmText,
			CloseButtonText = SettingsLocalization.Text("common.cancel"),
			DefaultButton = FAContentDialogButton.Primary,
		};
		FAContentDialogResult result = await dialog.ShowAsync(owner);
		return result == FAContentDialogResult.Primary
			? inputs.ToDictionary(item => item.Key, item => item.Value.Text ?? "")
			: null;
	}

	public static async Task ShowMessageAsync(Window owner, string title, string message)
	{
		FAContentDialog dialog = new()
		{
			Title = title,
			Content = new TextBlock {Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 520},
			CloseButtonText = SettingsLocalization.Text("common.close"),
			DefaultButton = FAContentDialogButton.Close,
		};
		await dialog.ShowAsync(owner);
	}
}
