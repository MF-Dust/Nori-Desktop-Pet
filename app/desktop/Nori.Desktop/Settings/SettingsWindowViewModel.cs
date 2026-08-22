using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
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
		Groups = BuildGroups();
		Navigation = Groups.SelectMany(group => group.Items).ToArray();
		_selectedItem = Navigation[0];
	}

	public SettingsOperations Operations { get; }

	public IReadOnlyList<SettingsNavigationGroup> Groups { get; private set; }

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
		Groups = BuildGroups();
		Navigation = Groups.SelectMany(group => group.Items).ToArray();
		SelectedItem = Navigation.FirstOrDefault(item => item.Key == selectedKey) ?? Navigation[0];
		OnPropertyChanged(nameof(Groups));
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

	private static IReadOnlyList<SettingsNavigationGroup> BuildGroups()
	{
		SettingsNavigationItem Ai(string key, string titleKey, FASymbol symbol) => new()
		{
			Key = key,
			Title = SettingsLocalization.Text(titleKey),
			IconSource = new FASymbolIconSource {Symbol = symbol},
		};

		return
		[
			new SettingsNavigationGroup
			{
				Key = "companion",
				Title = SettingsLocalization.Text("nav.group.companion"),
				IconSource = new FASymbolIconSource {Symbol = FASymbol.People},
				Items =
				[
					Ai("ai", "nav.ai", FASymbol.Message),
					Ai("voice", "nav.voice", FASymbol.Audio),
					Ai("proactive", "nav.proactive", FASymbol.Calendar),
					Ai("memory", "nav.memory", FASymbol.Library),
				],
			},
			new SettingsNavigationGroup
			{
				Key = "extensions",
				Title = SettingsLocalization.Text("nav.group.extensions"),
				IconSource = new FASymbolIconSource {Symbol = FASymbol.AllApps},
				Items =
				[
					Ai("skills", "nav.skills", FASymbol.AllApps),
					Ai("mcp", "nav.mcp", FASymbol.Code),
				],
			},
			new SettingsNavigationGroup
			{
				Key = "system",
				Title = SettingsLocalization.Text("nav.group.system"),
				IconSource = new FASymbolIconSource {Symbol = FASymbol.Settings},
				Items =
				[
					Ai("general", "nav.general", FASymbol.Settings),
					Ai("debug", "nav.debug", FASymbol.Repair),
				],
			},
		];
	}
}
