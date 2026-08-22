using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Nori.Desktop.Runtime;

namespace Nori.Desktop.Settings;

/// <summary>设置导航项。</summary>
public sealed class SettingsNavigationItem
{
	public required string Key { get; init; }
	public required string Title { get; init; }
	public required string Glyph { get; init; }
	public IBrush AccentBrush => SettingsLocalization.AccentBrush;
}

/// <summary>设置页面统一基类，提供深海 Fluent 卡片和异步错误出口。</summary>
public abstract class SettingsPageBase : UserControl
{
	private readonly Dictionary<string, (DispatcherTimer Timer, Action Action)> _debouncers = [];

	protected SettingsPageBase(SettingsWindowViewModel viewModel)
	{
		ViewModel = viewModel;
		Background = Brushes.Transparent;
	}

	protected SettingsWindowViewModel ViewModel { get; }
	protected SettingsOperations Operations => ViewModel.Operations;
	protected string T(string key) => SettingsLocalization.Text(key);
	protected Task<UiSnapshot> ReadSnapshotAsync() => Task.Run(Operations.Snapshot);

	public abstract string TitleKey { get; }
	public abstract string SubtitleKey { get; }

	/// <summary>页面首次显示或重新打开时刷新本页数据。</summary>
	public abstract Task RefreshAsync();

	/// <summary>窗口隐藏前提交当前页面尚未触发的文本写入。</summary>
	public virtual void FlushPending()
	{
		foreach ((DispatcherTimer timer, Action action) in _debouncers.Values.ToArray())
		{
			timer.Stop();
			action();
		}
		_debouncers.Clear();
	}

	protected void Debounce(string key, Action action)
	{
		if (_debouncers.TryGetValue(key, out (DispatcherTimer Timer, Action Action) old)) old.Timer.Stop();
		DispatcherTimer timer = new() {Interval = TimeSpan.FromMilliseconds(400)};
		timer.Tick += (_, _) =>
		{
			timer.Stop();
			_debouncers.Remove(key);
			RunBackground(action);
		};
		_debouncers[key] = (timer, action);
		timer.Start();
	}

	protected void RunBackground(Action action)
	{
		_ = Task.Run(action).ContinueWith(task =>
		{
			if (task.Exception is { } exception) ViewModel.ReportError(exception.GetBaseException());
		}, TaskScheduler.Default);
	}

	protected async Task RunAsync(Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception exception)
		{
			ViewModel.ReportError(exception);
		}
	}

	protected StackPanel CreateRoot()
	{
		StackPanel root = new()
		{
			Spacing = 14,
			Margin = new Thickness(0, 0, 8, 24),
		};
		Content = root;
		return root;
	}

	protected StackPanel CreateCard(StackPanel root, string title, string? description = null)
	{
		StackPanel body = new() {Spacing = 10};
		StackPanel heading = new() {Spacing = 3};
		heading.Children.Add(new TextBlock
		{
			Text = title,
			FontSize = 16,
			FontWeight = FontWeight.SemiBold,
			Foreground = Brush("#E8F6FF"),
		});
		if (!string.IsNullOrWhiteSpace(description))
		{
			heading.Children.Add(new TextBlock
			{
				Text = description,
				FontSize = 12,
				TextWrapping = TextWrapping.Wrap,
				Foreground = Brush("#8CA6B8"),
			});
		}
		body.Children.Add(heading);
		Border card = new()
		{
			Background = Brush("#0D2232"),
			BorderBrush = Brush("#1D4053"),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(10),
			Padding = new Thickness(18),
			Child = body,
		};
		root.Children.Add(card);
		return body;
	}

	protected TextBox AddTextField(StackPanel parent, string label, string value, Action<string>? onChanged = null, bool password = false, bool multiline = false, string? hint = null)
	{
		StackPanel field = new() {Spacing = 5};
		field.Children.Add(Label(label));
		TextBox textBox = new()
		{
			Text = value,
			PlaceholderText = hint,
			AcceptsReturn = multiline,
			TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
			MinHeight = multiline ? 96 : 34,
			VerticalContentAlignment = multiline ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Center,
		};
		if (password) textBox.PasswordChar = '•';
		if (multiline) textBox.MaxLines = 8;
		if (onChanged is not null) textBox.TextChanged += (_, _) => onChanged(textBox.Text ?? "");
		field.Children.Add(textBox);
		if (!string.IsNullOrWhiteSpace(hint) && multiline)
		{
			field.Children.Add(new TextBlock {Text = hint, FontSize = 11, Foreground = Brush("#718C9E"), TextWrapping = TextWrapping.Wrap});
		}
		parent.Children.Add(field);
		return textBox;
	}

	protected ComboBox AddComboField(StackPanel parent, string label, IReadOnlyList<string> items, int selectedIndex, Action<int>? onChanged = null)
	{
		StackPanel field = new() {Spacing = 5};
		field.Children.Add(Label(label));
		ComboBox combo = new()
		{
			ItemsSource = items,
			SelectedIndex = selectedIndex,
			HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
		};
		if (onChanged is not null) combo.SelectionChanged += (_, _) => onChanged(combo.SelectedIndex);
		field.Children.Add(combo);
		parent.Children.Add(field);
		return combo;
	}

	protected Button AddButton(StackPanel parent, string text, Action onClick, bool accent = false, bool danger = false)
	{
		Button button = new() {Content = text};
		if (accent) button.Classes.Add("accent");
		if (danger) button.Classes.Add("danger");
		button.Click += (_, _) => onClick();
		parent.Children.Add(button);
		return button;
	}

	protected ToggleSwitch AddSwitch(StackPanel parent, string title, string description, bool value, Action<bool> onChanged)
	{
		Grid row = new() {ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4)};
		StackPanel text = new() {Spacing = 3};
		text.Children.Add(new TextBlock {Text = title, FontSize = 13, Foreground = Brush("#E8F6FF")});
		text.Children.Add(new TextBlock {Text = description, FontSize = 11, Foreground = Brush("#8CA6B8"), TextWrapping = TextWrapping.Wrap});
		ToggleSwitch toggle = new() {IsChecked = value, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, OffContent = "", OnContent = ""};
		toggle.IsCheckedChanged += (_, _) => onChanged(toggle.IsChecked == true);
		row.Children.Add(text);
		Grid.SetColumn(toggle, 1);
		row.Children.Add(toggle);
		parent.Children.Add(row);
		return toggle;
	}

	protected TextBlock Label(string text) => new() {Text = text, FontSize = 12, Foreground = Brush("#A9C0CE")};

	protected TextBlock Hint(string text) => new() {Text = text, FontSize = 12, Foreground = Brush("#8CA6B8"), TextWrapping = TextWrapping.Wrap};

	protected static SolidColorBrush Brush(string value) => new(Color.Parse(value));

	protected static Border Separator() => new() {Height = 1, Background = Brush("#1D4053"), Margin = new Thickness(0, 5)};
}
