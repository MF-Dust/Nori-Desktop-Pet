using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nori.Desktop.Settings;

/// <summary>设置页统一的确认与表单对话框。</summary>
public static class SettingsDialogs
{
	public sealed record FormField(string Key, string Label, string Value = "", bool Multiline = false, bool Password = false);

	public static async Task<bool> ConfirmAsync(Window owner, string title, string message, bool danger = false)
	{
		Window dialog = CreateDialog(owner, title, 420, 230);
		StackPanel content = DialogContent(dialog);
		content.Children.Add(new TextBlock
		{
			Text = message,
			TextWrapping = TextWrapping.Wrap,
			Foreground = Brush("#C5D8E3"),
			FontSize = 13,
		});
		StackPanel buttons = ButtonRow();
		Button cancel = new() {Content = SettingsLocalization.Text("common.cancel")};
		Button confirm = new() {Content = danger ? SettingsLocalization.Text("common.confirm") : SettingsLocalization.Text("common.save")};
		if (danger) confirm.Classes.Add("danger"); else confirm.Classes.Add("accent");
		cancel.Click += (_, _) => dialog.Close(false);
		confirm.Click += (_, _) => dialog.Close(true);
		buttons.Children.Add(cancel);
		buttons.Children.Add(confirm);
		content.Children.Add(buttons);
		return await dialog.ShowDialog<bool>(owner);
	}

	public static async Task<IReadOnlyDictionary<string, string>?> FormAsync(Window owner, string title, IReadOnlyList<FormField> fields, string confirmText)
	{
		Window dialog = CreateDialog(owner, title, 560, 680);
		StackPanel content = DialogContent(dialog);
		Dictionary<string, TextBox> inputs = [];
		ScrollViewer scroll = new() {MaxHeight = 500};
		StackPanel form = new() {Spacing = 10};
		foreach (FormField field in fields)
		{
			StackPanel row = new() {Spacing = 4};
			row.Children.Add(new TextBlock {Text = field.Label, Foreground = Brush("#A9C0CE"), FontSize = 12});
			TextBox input = new()
			{
				Text = field.Value,
				AcceptsReturn = field.Multiline,
				TextWrapping = field.Multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
				MinHeight = field.Multiline ? 100 : 34,
				VerticalContentAlignment = field.Multiline ? VerticalAlignment.Top : VerticalAlignment.Center,
			};
			if (field.Password) input.PasswordChar = '•';
			inputs[field.Key] = input;
			row.Children.Add(input);
			form.Children.Add(row);
		}
		scroll.Content = form;
		content.Children.Add(scroll);
		StackPanel buttons = ButtonRow();
		Button cancel = new() {Content = SettingsLocalization.Text("common.cancel")};
		Button confirm = new() {Content = confirmText};
		confirm.Classes.Add("accent");
		cancel.Click += (_, _) => dialog.Close(null);
		confirm.Click += (_, _) => dialog.Close(inputs.ToDictionary(item => item.Key, item => item.Value.Text ?? ""));
		buttons.Children.Add(cancel);
		buttons.Children.Add(confirm);
		content.Children.Add(buttons);
		return await dialog.ShowDialog<IReadOnlyDictionary<string, string>?>(owner);
	}

	public static async Task ShowMessageAsync(Window owner, string title, string message)
	{
		Window dialog = CreateDialog(owner, title, 460, 260);
		StackPanel content = DialogContent(dialog);
		content.Children.Add(new TextBlock {Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#C5D8E3"), FontSize = 13});
		Button close = new() {Content = SettingsLocalization.Text("common.close")};
		close.Classes.Add("accent");
		close.HorizontalAlignment = HorizontalAlignment.Right;
		close.Click += (_, _) => dialog.Close();
		content.Children.Add(close);
		await dialog.ShowDialog(owner);
	}

	private static Window CreateDialog(Window owner, string title, double width, double height) => new()
	{
		Title = title,
		Width = width,
		Height = height,
		MinWidth = width,
		MinHeight = height,
		MaxWidth = width,
		MaxHeight = height,
		WindowStartupLocation = WindowStartupLocation.CenterOwner,
		CanResize = false,
		Background = Brush("#0B2030"),
		DataContext = owner,
	};

	private static StackPanel DialogContent(Window dialog)
	{
		StackPanel content = new() {Spacing = 16, Margin = new Thickness(24)};
		dialog.Content = content;
		return content;
	}

	private static StackPanel ButtonRow() => new()
	{
		Orientation = Orientation.Horizontal,
		HorizontalAlignment = HorizontalAlignment.Right,
		Spacing = 8,
	};

	private static SolidColorBrush Brush(string value) => new(Color.Parse(value));
}
