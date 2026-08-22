using Avalonia.Controls;
using Nori.Desktop.Runtime;

namespace Nori.Desktop.Settings;

/// <summary>系统常规设置页。</summary>
public sealed class GeneralSettingsPage : SettingsPageBase
{
	private ComboBox _language = null!;
	private ToggleSwitch _autoSummon = null!;
	private TextBlock _version = null!;
	private TextBlock _renderer = null!;
	private bool _synchronizing;

	public GeneralSettingsPage(SettingsWindowViewModel viewModel) : base(viewModel)
	{
		Build();
	}

	public override string TitleKey => "general.title";
	public override string SubtitleKey => "general.subtitle";

	public override async Task RefreshAsync()
	{
		UiSnapshot snapshot = await ReadSnapshotAsync();
		_synchronizing = true;
		try
		{
			_language.SelectedIndex = string.Equals(snapshot.General.Language, "en-US", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
			_autoSummon.IsChecked = snapshot.General.PetAutoSummon;
			_version.Text = snapshot.App.AppVersion;
			_renderer.Text = "Avalonia UI 12 + 原生桌面窗口";
		}
		finally { _synchronizing = false; }
	}

	private void Build()
	{
		StackPanel root = CreateRoot();
		StackPanel language = CreateCard(root, T("general.language"));
		_language = AddComboField(language, T("general.language"), [T("general.chinese"), T("general.english")], 0, index =>
		{
			if (_synchronizing || index < 0) return;
			string value = index == 1 ? "en-US" : "zh-CN";
			SettingsLocalization.SetLanguage(value);
			RunBackground(() => Operations.UpdateGeneral(new GeneralSettingsPatch {Language = value}));
		});

		StackPanel startup = CreateCard(root, T("general.startup"));
		_autoSummon = AddSwitch(startup, T("general.autoSummon"), T("general.autoSummonDesc"), true, value =>
		{
			if (!_synchronizing) RunBackground(() => Operations.UpdateGeneral(new GeneralSettingsPatch {PetAutoSummon = value}));
		});

		StackPanel about = CreateCard(root, T("general.about"));
		AddInfo(about, T("general.version"), out _version);
		AddInfo(about, T("general.license"), out TextBlock license);
		license.Text = "GPL-3.0";
		AddInfo(about, T("general.renderer"), out _renderer);
	}

	private static void AddInfo(StackPanel parent, string label, out TextBlock value)
	{
		Grid row = new() {ColumnDefinitions = new ColumnDefinitions("*,Auto")};
		row.Children.Add(new TextBlock {Text = label, Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#A9C0CE")), FontSize = 12});
		value = new TextBlock {Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E8F6FF")), FontSize = 12};
		Grid.SetColumn(value, 1);
		row.Children.Add(value);
		parent.Children.Add(row);
	}
}
