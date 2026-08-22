using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Nori.Desktop.Runtime;

namespace Nori.Desktop.Settings;

/// <summary>设置导航项。</summary>
public sealed class SettingsNavigationItem
{
	public required string Key { get; init; }
	public required string Title { get; init; }
	public required FAIconSource IconSource { get; init; }
	public IBrush AccentBrush => SettingsLocalization.AccentBrush;
}

/// <summary>设置导航分组。</summary>
public sealed class SettingsNavigationGroup
{
	public required string Key { get; init; }
	public required string Title { get; init; }
	public required FAIconSource IconSource { get; init; }
	public required IReadOnlyList<SettingsNavigationItem> Items { get; init; }
}

/// <summary>设置页面统一基类，提供 ClassIsland 风格的 Fluent 设置项和异步错误出口。</summary>
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
			Spacing = 4,
			Margin = new Thickness(12, 12, 18, 12),
			MaxWidth = 960,
		};
		root.Classes.Add("settings-container");
		Content = root;
		return root;
	}

	protected StackPanel CreateCard(StackPanel root, string title, string? description = null)
	{
		FASettingsExpander expander = new()
		{
			Header = title,
			Description = description ?? "",
			IsExpanded = true,
		};
		SettingsItemsHost body = new(expander);
		root.Children.Add(expander);
		return body;
	}

	protected TextBox AddTextField(StackPanel parent, string label, string value, Action<string>? onChanged = null, bool password = false, bool multiline = false, string? hint = null)
	{
		TextBox textBox = new()
		{
			Text = value,
			PlaceholderText = hint,
			AcceptsReturn = multiline,
			TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
			MinHeight = multiline ? 100 : 32,
			MinWidth = multiline ? 320 : 180,
			MaxWidth = 480,
			HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
			VerticalContentAlignment = multiline ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Center,
		};
		if (password) textBox.PasswordChar = '•';
		if (multiline) textBox.MaxLines = 8;
		if (onChanged is not null) textBox.TextChanged += (_, _) => onChanged(textBox.Text ?? "");
		FASettingsExpanderItem item = new()
		{
			Content = label,
			Description = multiline ? hint : null,
			Footer = textBox,
		};
		parent.Children.Add(item);
		return textBox;
	}

	protected ComboBox AddComboField(StackPanel parent, string label, IReadOnlyList<string> items, int selectedIndex, Action<int>? onChanged = null)
	{
		ComboBox combo = new()
		{
			ItemsSource = items,
			SelectedIndex = selectedIndex,
			MinWidth = 180,
			MaxWidth = 360,
			HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
			HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
		};
		if (onChanged is not null) combo.SelectionChanged += (_, _) => onChanged(combo.SelectedIndex);
		FASettingsExpanderItem item = new()
		{
			Content = label,
			Footer = combo,
		};
		parent.Children.Add(item);
		return combo;
	}

	protected Button AddButton(StackPanel parent, string text, Action onClick, bool accent = false, bool danger = false)
	{
		Button button = new()
		{
			Content = text,
			HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
			MinHeight = 32,
		};
		if (accent) button.Classes.Add("accent");
		if (danger) button.Classes.Add("danger");
		button.Click += (_, _) => onClick();
		parent.Children.Add(button);
		return button;
	}

	protected ToggleSwitch AddSwitch(StackPanel parent, string title, string description, bool value, Action<bool> onChanged)
	{
		ToggleSwitch toggle = new()
		{
			IsChecked = value,
			OnContent = T("common.on"),
			OffContent = T("common.off"),
			VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
		};
		toggle.IsCheckedChanged += (_, _) => onChanged(toggle.IsChecked == true);
		FASettingsExpanderItem item = new()
		{
			Content = title,
			Description = description,
			Footer = toggle,
		};
		parent.Children.Add(item);
		return toggle;
	}

	protected TextBlock Label(string text) => new() {Text = text, FontSize = 12, Foreground = Brush("#A9C0CE")};

	protected TextBlock Hint(string text) => new() {Text = text, FontSize = 12, Foreground = Brush("#8CA6B8"), TextWrapping = TextWrapping.Wrap};

	protected static IBrush Brush(string value) => SettingsTheme.FromLegacy(value);

	protected static Control Separator() => new Avalonia.Controls.Separator
	{
		Margin = new Thickness(0, 12, 0, 4),
	};

	private sealed class SettingsItemsHost : StackPanel
	{
		private readonly FASettingsExpander _owner;
		private bool _redirecting;

		public SettingsItemsHost(FASettingsExpander owner)
		{
			_owner = owner;
			Children.CollectionChanged += OnChildrenChanged;
		}

		private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (_redirecting || e.NewItems is null) return;
			foreach (object item in e.NewItems)
			{
				if (item is not Control control) continue;
				_redirecting = true;
				try
				{
					Children.Remove(control);
					_owner.Items.Add(control);
				}
				finally
				{
					_redirecting = false;
				}
			}
		}
	}
}