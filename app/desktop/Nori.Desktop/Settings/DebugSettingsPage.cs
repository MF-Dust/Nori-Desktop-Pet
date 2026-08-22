using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Nori.Desktop.Runtime;

namespace Nori.Desktop.Settings;

/// <summary>调试与诊断设置页。</summary>
public sealed class DebugSettingsPage : SettingsPageBase
{
	private StackPanel _diagnostic = null!;
	private StackPanel _logs = null!;
	private ComboBox _filter = null!;
	private IReadOnlyList<LogSnapshot> _logItems = [];

	public DebugSettingsPage(SettingsWindowViewModel viewModel) : base(viewModel)
	{
		Build();
	}

	public override string TitleKey => "debug.title";
	public override string SubtitleKey => "debug.subtitle";

	public override Task RefreshAsync()
	{
		RebuildDiagnostic(Operations.DiagnosticInfo());
		RebuildLogs();
		return Task.CompletedTask;
	}

	private void Build()
	{
		StackPanel root = CreateRoot();
		Border warning = new()
		{
			Background = Brush("#3D2630"),
			BorderBrush = Brush("#9A5564"),
			BorderThickness = new Thickness(1),
			Padding = new Thickness(14),
			CornerRadius = new CornerRadius(8),
			Child = Hint(T("debug.warning")),
		};
		root.Children.Add(warning);

		StackPanel diagnosticCard = CreateCard(root, T("debug.diagnostic"));
		Grid diagnosticActions = new() {ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 6};
		Button refresh = new() {Content = T("debug.refresh")};
		refresh.Click += (_, _) => RebuildDiagnostic(Operations.DiagnosticInfo());
		Grid.SetColumn(refresh, 1);
		diagnosticActions.Children.Add(refresh);
		Button copy = new() {Content = T("debug.copy")};
		copy.Click += async (_, _) => await CopyDiagnosticAsync();
		Grid.SetColumn(copy, 2);
		diagnosticActions.Children.Add(copy);
		Button folder = new() {Content = T("debug.openFolder")};
		folder.Click += (_, _) => RunBackground(Operations.OpenLogFolder);
		Grid.SetColumn(folder, 3);
		diagnosticActions.Children.Add(folder);
		diagnosticCard.Children.Add(diagnosticActions);
		_diagnostic = new StackPanel {Spacing = 5};
		diagnosticCard.Children.Add(_diagnostic);

		StackPanel logCard = CreateCard(root, T("debug.logs"));
		Grid logToolbar = new() {ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 6};
		_filter = new ComboBox {ItemsSource = new[] {T("common.all"), "error", "warn", "info"}, SelectedIndex = 0, Width = 120};
		_filter.SelectionChanged += (_, _) => RebuildLogs();
		logToolbar.Children.Add(_filter);
		Button refreshLogs = new() {Content = T("debug.refresh")};
		refreshLogs.Click += (_, _) => { _logItems = Operations.RecentLogs(); RebuildLogs(); };
		Grid.SetColumn(refreshLogs, 1);
		logToolbar.Children.Add(refreshLogs);
		Button clear = new() {Content = T("debug.clear")};
		clear.Click += async (_, _) =>
		{
			if (!await SettingsDialogs.ConfirmAsync(Owner(), T("debug.clear"), T("debug.clearConfirm"), true)) return;
			Operations.ClearRecentLogs();
			_logItems = [];
			RebuildLogs();
		};
		Grid.SetColumn(clear, 2);
		logToolbar.Children.Add(clear);
		Button copyLogs = new() {Content = T("debug.copy")};
		copyLogs.Click += async (_, _) => await CopyLogsAsync();
		Grid.SetColumn(copyLogs, 3);
		logToolbar.Children.Add(copyLogs);
		logCard.Children.Add(logToolbar);
		ScrollViewer logScroll = new() {Height = 230};
		_logs = new StackPanel {Spacing = 2};
		logScroll.Content = _logs;
		logCard.Children.Add(logScroll);

		StackPanel actions = CreateCard(root, T("debug.features"));
		Button gc = new() {Content = T("debug.gc")};
		gc.Click += async (_, _) =>
		{
			gc.IsEnabled = false;
			try
			{
				long released = await Task.Run(Operations.CollectGarbage);
				ViewModel.ReportError(new InvalidOperationException($"已释放 {released} 字节"));
				_logItems = Operations.RecentLogs();
				RebuildLogs();
			}
			finally { gc.IsEnabled = true; }
		};
		actions.Children.Add(gc);
		Button testLog = new() {Content = T("debug.testLog")};
		testLog.Click += (_, _) => { Operations.WriteTestLog(); _logItems = Operations.RecentLogs(); RebuildLogs(); };
		actions.Children.Add(testLog);

		StackPanel danger = CreateCard(root, T("debug.danger"));
		AddCrashButton(danger, T("debug.crashUi"), "ui_thread", false);
		AddCrashButton(danger, T("debug.crashBackground"), "background_thread", true);
		AddCrashButton(danger, T("debug.crashTask"), "unobserved_task", false);
	}

	private void AddCrashButton(StackPanel parent, string title, string mode, bool exits)
	{
		Button button = new() {Content = title};
		button.Classes.Add("danger");
		button.Click += async (_, _) =>
		{
			string message = exits ? T("debug.crashExitConfirm") : T("debug.crashConfirm");
			if (!await SettingsDialogs.ConfirmAsync(Owner(), title, message, true)) return;
			Operations.TriggerCrashTest(mode);
		};
		parent.Children.Add(button);
	}

	private void RebuildDiagnostic(IReadOnlyDictionary<string, string> values)
	{
		if (_diagnostic is null) return;
		_diagnostic.Children.Clear();
		foreach ((string key, string value) in values)
		{
			Grid row = new() {ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 18};
			row.Children.Add(new TextBlock {Text = key, Foreground = Brush("#8CA6B8"), FontSize = 11});
			TextBlock text = new() {Text = value, Foreground = Brush("#E8F6FF"), FontSize = 11, TextWrapping = TextWrapping.Wrap};
			Grid.SetColumn(text, 1);
			row.Children.Add(text);
			_diagnostic.Children.Add(row);
		}
	}

	private void RebuildLogs()
	{
		if (_logs is null) return;
		if (_logItems.Count == 0) _logItems = Operations.RecentLogs();
		_logs.Children.Clear();
		string filter = _filter.SelectedIndex switch {1 => "error", 2 => "warn", 3 => "info", _ => ""};
		IEnumerable<LogSnapshot> items = filter.Length == 0 ? _logItems : _logItems.Where(item => item.Level == filter);
		foreach (LogSnapshot item in items)
		{
			TextBlock row = new()
			{
				Text = $"[{item.Time}] [{item.Level}] [{item.Source}] {item.Message}",
				FontFamily = new Avalonia.Media.FontFamily("Consolas"),
				FontSize = 11,
				TextWrapping = TextWrapping.Wrap,
				Foreground = item.Level == "error" ? Brush("#FF9BA7") : item.Level == "warn" ? Brush("#FFD895") : Brush("#B7D8E8"),
			};
			_logs.Children.Add(row);
		}
		if (_logs.Children.Count == 0) _logs.Children.Add(Hint(T("debug.noLogs")));
	}

	private async Task CopyDiagnosticAsync()
	{
		Dictionary<string, string> values = Operations.DiagnosticInfo();
		await CopyAsync(string.Join(Environment.NewLine, values.Select(item => $"{item.Key}: {item.Value}")));
	}

	private async Task CopyLogsAsync()
	{
		await CopyAsync(string.Join(Environment.NewLine, _logItems.Select(item => $"[{item.Time}] [{item.Level}] [{item.Source}] {item.Message}")));
	}

	private async Task CopyAsync(string text)
	{
		IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
		if (clipboard is not null) await clipboard.SetTextAsync(text);
	}

	private Window Owner() => (TopLevel.GetTopLevel(this) as Window)!;
}
