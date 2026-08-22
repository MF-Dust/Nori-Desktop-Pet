using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nori.Desktop.Runtime;

namespace Nori.Desktop.Settings;

/// <summary>主动交互与提醒设置页。</summary>
public sealed class ProactiveSettingsPage : SettingsPageBase
{
	private ToggleSwitch _idleEnabled = null!;
	private ToggleSwitch _dailyGreeting = null!;
	private ComboBox _interval = null!;
	private TextBox _reminderText = null!;
	private ComboBox _reminderDelay = null!;
	private StackPanel _reminderList = null!;
	private bool _synchronizing;
	private static readonly int[] IntervalValues = [5, 15, 30, 60];
	private static readonly int[] ReminderValues = [5, 15, 30, 60, 120];

	public ProactiveSettingsPage(SettingsWindowViewModel viewModel) : base(viewModel)
	{
		Build();
	}

	public override string TitleKey => "proactive.title";
	public override string SubtitleKey => "proactive.subtitle";

	public override async Task RefreshAsync()
	{
		UiSnapshot snapshot = await ReadSnapshotAsync();
		_synchronizing = true;
		try
		{
			_idleEnabled.IsChecked = snapshot.Proactive.IdleEnabled;
			_interval.SelectedIndex = ClosestIndex(IntervalValues, snapshot.Proactive.IdleMinutes);
			_dailyGreeting.IsChecked = snapshot.Proactive.DailyGreeting;
			RebuildReminders(snapshot.Proactive.Reminders);
		}
		finally
		{
			_synchronizing = false;
		}
	}

	private void Build()
	{
		StackPanel root = CreateRoot();
		StackPanel idle = CreateCard(root, T("proactive.idle"));
		_idleEnabled = AddSwitch(idle, T("proactive.idleEnabled"), T("proactive.idleDesc"), true, value =>
		{
			if (!_synchronizing) RunBackground(() => Operations.UpdateProactive(new ProactiveSettingsPatch {IdleEnabled = value}));
		});
		_interval = AddComboField(idle, T("proactive.interval"), new[] {T("proactive.fiveMinutes"), T("proactive.fifteenMinutes"), T("proactive.thirtyMinutes"), T("proactive.oneHour")}, 1, index =>
		{
			if (!_synchronizing && index >= 0) RunBackground(() => Operations.UpdateProactive(new ProactiveSettingsPatch {IdleMinutes = IntervalValues[index]}));
		});

		StackPanel daily = CreateCard(root, T("proactive.daily"));
		_dailyGreeting = AddSwitch(daily, T("proactive.dailyEnabled"), T("proactive.dailyDesc"), true, value =>
		{
			if (!_synchronizing) RunBackground(() => Operations.UpdateProactive(new ProactiveSettingsPatch {DailyGreeting = value}));
		});

		StackPanel reminders = CreateCard(root, T("proactive.reminders"));
		Grid addRow = new() {ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8};
		_reminderText = new TextBox {PlaceholderText = "喝水 / 站起来走走 / 准备开会…"};
		_reminderText.KeyDown += async (_, args) =>
		{
			if (args.Key == Avalonia.Input.Key.Enter) await AddReminderAsync();
		};
		addRow.Children.Add(_reminderText);
		_reminderDelay = new ComboBox
		{
			ItemsSource = new[] {T("proactive.fiveLater"), T("proactive.fifteenLater"), T("proactive.thirtyLater"), T("proactive.oneHourLater"), T("proactive.twoHoursLater")},
			SelectedIndex = 1,
			Width = 120,
		};
		Grid.SetColumn(_reminderDelay, 1);
		addRow.Children.Add(_reminderDelay);
		Button add = new() {Content = T("proactive.add")};
		add.Classes.Add("accent");
		add.Click += async (_, _) => await AddReminderAsync();
		Grid.SetColumn(add, 2);
		addRow.Children.Add(add);
		reminders.Children.Add(addRow);
		_reminderList = new StackPanel {Spacing = 6};
		reminders.Children.Add(_reminderList);
	}

	private async Task AddReminderAsync()
	{
		string content = _reminderText.Text?.Trim() ?? "";
		if (content.Length == 0) return;
		int index = Math.Clamp(_reminderDelay.SelectedIndex, 0, ReminderValues.Length - 1);
		try
		{
			await Task.Run(() => Operations.AddReminder(content, ReminderValues[index]));
			_reminderText.Text = "";
			await RefreshAsync();
		}
		catch (Exception exception)
		{
			ViewModel.ReportError(exception);
		}
	}

	private void RebuildReminders(IReadOnlyList<ReminderSnapshot> items)
	{
		_reminderList.Children.Clear();
		if (items.Count == 0)
		{
			_reminderList.Children.Add(Hint(T("proactive.empty")));
			return;
		}
		foreach (ReminderSnapshot item in items)
		{
			Grid row = new() {ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2)};
			StackPanel text = new() {Spacing = 2};
			text.Children.Add(new TextBlock {Text = item.Content, Foreground = Brush("#E8F6FF"), FontSize = 13});
			text.Children.Add(new TextBlock {Text = DateTimeOffset.FromUnixTimeMilliseconds(item.TriggerTime).ToLocalTime().ToString("g"), Foreground = Brush("#8CA6B8"), FontSize = 11});
			row.Children.Add(text);
			Button cancel = new() {Content = "×"};
			cancel.Classes.Add("danger");
			cancel.Click += async (_, _) =>
			{
				if (!await SettingsDialogs.ConfirmAsync(Owner(), T("common.cancel"), T("proactive.cancelConfirm").Replace("{0}", item.Content, StringComparison.Ordinal))) return;
				RunBackground(() => Operations.CancelReminder(item.Id));
				await Task.Delay(80);
				await RefreshAsync();
			};
			Grid.SetColumn(cancel, 1);
			row.Children.Add(cancel);
			_reminderList.Children.Add(row);
			_reminderList.Children.Add(Separator());
		}
	}

	private Window Owner() => (TopLevel.GetTopLevel(this) as Window)!;

	private static int ClosestIndex(IReadOnlyList<int> values, int value)
	{
		int best = 0;
		for (int i = 1; i < values.Count; i++) if (Math.Abs(values[i] - value) < Math.Abs(values[best] - value)) best = i;
		return best;
	}
}
