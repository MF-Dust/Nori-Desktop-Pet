using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Nori.Desktop.Runtime;

namespace Nori.Desktop.Settings;

/// <summary>原生设置窗口壳 ViewModel。</summary>
public sealed class SettingsWindowViewModel : ObservableObject
{
	private readonly Dictionary<string, SettingsPageBase> _pages = [];
	private SettingsNavigationItem? _selectedItem;
	private string _errorMessage = "";

	public SettingsWindowViewModel(AppRuntime runtime)
	{
		Operations = runtime.Settings;
		Navigation = BuildNavigation();
		_selectedItem = Navigation[0];
	}

	public SettingsOperations Operations { get; }

	public IReadOnlyList<SettingsNavigationItem> Navigation { get; private set; }

	public SettingsNavigationItem? SelectedItem
	{
		get => _selectedItem;
		set => SetProperty(ref _selectedItem, value);
	}

	public string ErrorMessage
	{
		get => _errorMessage;
		private set => SetProperty(ref _errorMessage, value);
	}

	public event Action? NavigationChanged;

	public void RebuildNavigation()
	{
		string? selectedKey = SelectedItem?.Key;
		Navigation = BuildNavigation();
		SelectedItem = Navigation.FirstOrDefault(item => item.Key == selectedKey) ?? Navigation[0];
		OnPropertyChanged(nameof(Navigation));
		NavigationChanged?.Invoke();
	}

	public SettingsPageBase RecreatePage(string key)
	{
		if (_pages.Remove(key, out SettingsPageBase? page)) page.FlushPending();
		return GetPage(key);
	}

	public SettingsPageBase GetPage(string key)
	{
		if (_pages.TryGetValue(key, out SettingsPageBase? page)) return page;
		page = key switch
		{
			"ai" => new AiSettingsPage(this),
			"voice" => new VoiceSettingsPage(this),
			"proactive" => new ProactiveSettingsPage(this),
			"memory" => new MemorySettingsPage(this),
			"skills" => new SkillsSettingsPage(this),
			"mcp" => new McpSettingsPage(this),
			"general" => new GeneralSettingsPage(this),
			"debug" => new DebugSettingsPage(this),
			_ => new AiSettingsPage(this),
		};
		_pages[key] = page;
		return page;
	}

	public void FlushPending()
	{
		foreach (SettingsPageBase page in _pages.Values) page.FlushPending();
	}

	public void ReportError(Exception exception)
	{
		string message = exception.Message;
		Avalonia.Threading.Dispatcher.UIThread.Post(() => ErrorMessage = message);
	}

	public void ClearError() => ErrorMessage = "";

	private static IReadOnlyList<SettingsNavigationItem> BuildNavigation() =>
	[
		new() {Key = "ai", Title = SettingsLocalization.Text("nav.ai"), Glyph = "◈"},
		new() {Key = "voice", Title = SettingsLocalization.Text("nav.voice"), Glyph = "♫"},
		new() {Key = "proactive", Title = SettingsLocalization.Text("nav.proactive"), Glyph = "✦"},
		new() {Key = "memory", Title = SettingsLocalization.Text("nav.memory"), Glyph = "▣"},
		new() {Key = "skills", Title = SettingsLocalization.Text("nav.skills"), Glyph = "✧"},
		new() {Key = "mcp", Title = SettingsLocalization.Text("nav.mcp"), Glyph = "⌘"},
		new() {Key = "general", Title = SettingsLocalization.Text("nav.general"), Glyph = "⚙"},
		new() {Key = "debug", Title = SettingsLocalization.Text("nav.debug"), Glyph = "⌁"},
	];
}
