using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nori.Core.Skills;
using Nori.Desktop.Runtime;

namespace Nori.Desktop.Settings;

/// <summary>技能工坊设置页。</summary>
public sealed class SkillsSettingsPage : SettingsPageBase
{
	private static readonly string[] Categories = ["all", "productivity", "coding", "life", "roleplay", "entertainment"];
	private Button _installedTab = null!;
	private Button _marketTab = null!;
	private TextBox _search = null!;
	private ComboBox _category = null!;
	private StackPanel _list = null!;
	private IReadOnlyList<SkillSnapshot> _installed = [];
	private IReadOnlyList<SkillRecord> _market = [];
	private bool _marketMode;

	public SkillsSettingsPage(SettingsWindowViewModel viewModel) : base(viewModel)
	{
		Build();
	}

	public override string TitleKey => "skills.title";
	public override string SubtitleKey => "skills.subtitle";

	public override async Task RefreshAsync()
	{
		UiSnapshot snapshot = await ReadSnapshotAsync();
		_installed = snapshot.Skills;
		_market = Operations.MarketplaceSkills();
		Rebuild();
	}

	private void Build()
	{
		StackPanel root = CreateRoot();
		Grid toolbar = new() {ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), ColumnSpacing = 8};
		_installedTab = new Button {Content = T("skills.installed")};
		_marketTab = new Button {Content = T("skills.market")};
		_installedTab.Click += (_, _) => { _marketMode = false; Rebuild(); };
		_marketTab.Click += (_, _) => { _marketMode = true; Rebuild(); };
		toolbar.Children.Add(_installedTab);
		Grid.SetColumn(_marketTab, 1);
		toolbar.Children.Add(_marketTab);
		_search = new TextBox {PlaceholderText = T("skills.search")};
		_search.TextChanged += (_, _) => Rebuild();
		Grid.SetColumn(_search, 2);
		toolbar.Children.Add(_search);
		_category = new ComboBox
		{
			ItemsSource = new[] {T("skills.all"), T("skills.productivity"), T("skills.coding"), T("skills.life"), T("skills.roleplay"), T("skills.entertainment")},
			SelectedIndex = 0,
			Width = 145,
		};
		_category.SelectionChanged += (_, _) => Rebuild();
		Grid.SetColumn(_category, 3);
		toolbar.Children.Add(_category);
		Button url = new() {Content = T("skills.url")};
		url.Click += async (_, _) => await InstallUrlAsync();
		Grid.SetColumn(url, 4);
		toolbar.Children.Add(url);
		root.Children.Add(toolbar);
		Button create = new() {Content = T("skills.new"), HorizontalAlignment = HorizontalAlignment.Left};
		create.Classes.Add("accent");
		create.Click += async (_, _) => await EditSkillAsync(null);
		root.Children.Add(create);
		_list = new StackPanel {Spacing = 8};
		root.Children.Add(_list);
	}

	private void Rebuild()
	{
		if (_list is null) return;
		_installedTab.Classes.Set("accent", !_marketMode);
		_marketTab.Classes.Set("accent", _marketMode);
		_list.Children.Clear();
		string query = _search.Text?.Trim().ToLowerInvariant() ?? "";
		string category = Categories[Math.Clamp(_category.SelectedIndex, 0, Categories.Length - 1)];
		if (_marketMode)
		{
			IEnumerable<SkillRecord> items = _market.Where(item => Matches(item.Name, item.Description, item.Tags, query, category, item.Category));
			foreach (SkillRecord skill in items) AddMarketCard(skill);
		}
		else
		{
			IEnumerable<SkillSnapshot> items = _installed.Where(item => Matches(item.Name, item.Description, item.Tags, query, category, item.Category));
			foreach (SkillSnapshot skill in items) AddInstalledCard(skill);
		}
		if (_list.Children.Count == 0) _list.Children.Add(Hint(T("skills.empty")));
	}

	private void AddInstalledCard(SkillSnapshot skill)
	{
		StackPanel body = CardBody(skill.Name, $"{skill.Author} · v{skill.Version} · {skill.Source}");
		body.Children.Add(new TextBlock {Text = skill.Description, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#A9C0CE"), FontSize = 12});
		StackPanel buttons = new() {Orientation = Orientation.Horizontal, Spacing = 6};
		ToggleSwitch enabled = new() {IsChecked = skill.Enabled, OnContent = T("skills.toggle"), OffContent = T("common.none")};
		enabled.IsCheckedChanged += (_, _) => RunBackground(() => Operations.ToggleSkill(skill.Id, enabled.IsChecked == true));
		buttons.Children.Add(enabled);
		Button details = new() {Content = T("skills.detail")};
		details.Click += async (_, _) => await ShowDetailsAsync(skill);
		buttons.Children.Add(details);
		Button edit = new() {Content = T("skills.edit")};
		edit.Click += async (_, _) => await EditSkillAsync(skill);
		buttons.Children.Add(edit);
		if (!string.Equals(skill.Source, "builtin", StringComparison.OrdinalIgnoreCase))
		{
			Button uninstall = new() {Content = T("skills.uninstall")};
			uninstall.Classes.Add("danger");
			uninstall.Click += async (_, _) =>
			{
				if (!await SettingsDialogs.ConfirmAsync(Owner(), T("skills.uninstall"), T("skills.uninstallConfirm").Replace("{0}", skill.Name, StringComparison.Ordinal), true)) return;
				Operations.UninstallSkill(skill.Id);
				await RefreshAsync();
			};
			buttons.Children.Add(uninstall);
		}
		body.Children.Add(buttons);
	}

	private void AddMarketCard(SkillRecord skill)
	{
		StackPanel body = CardBody(skill.Name, $"{skill.Author} · v{skill.Version}");
		body.Children.Add(new TextBlock {Text = skill.Description, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#A9C0CE"), FontSize = 12});
		Button install = new() {Content = T("skills.install")};
		install.Classes.Add("accent");
		install.Click += async (_, _) =>
		{
			install.IsEnabled = false;
			try { Operations.InstallMarketplaceSkill(skill.Id); await RefreshAsync(); }
			catch (Exception exception) { ViewModel.ReportError(exception); install.IsEnabled = true; }
		};
		body.Children.Add(install);
	}

	private async Task InstallUrlAsync()
	{
		IReadOnlyDictionary<string, string>? form = await SettingsDialogs.FormAsync(Owner(), T("skills.url"),
			[new SettingsDialogs.FormField("url", T("skills.remoteUrl"), "https://raw.githubusercontent.com/…/SKILL.md")], T("skills.install"));
		string? url = form?["url"];
		if (string.IsNullOrWhiteSpace(url)) return;
		try
		{
			await Operations.InstallSkillFromUrlAsync(url.Trim());
			await RefreshAsync();
		}
		catch (Exception exception) { ViewModel.ReportError(exception); }
	}

	private async Task EditSkillAsync(SkillSnapshot? source)
	{
		SkillRecord? current = null;
		if (source is not null)
		{
			try
			{
				string json = Operations.ExportSkill(source.Id);
				current = JsonSerializer.Deserialize<SkillRecord>(json);
			}
			catch (Exception exception) { ViewModel.ReportError(exception); return; }
		}
		current ??= new SkillRecord
		{
			Id = $"skill_custom_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}",
			Name = "",
			Description = "",
			Author = SettingsLocalization.IsEnglish ? "Me" : "我",
			Version = "1.0.0",
			Category = "productivity",
			Instructions = "",
			Tags = [],
			Tools = [],
			Enabled = true,
			Source = "custom",
		};
		IReadOnlyDictionary<string, string>? form = await SettingsDialogs.FormAsync(Owner(), source is null ? T("skills.new") : T("skills.edit"),
		[
			new("name", T("skills.formName"), current.Name),
			new("description", T("skills.formDescription"), current.Description),
			new("author", T("skills.formAuthor"), current.Author),
			new("version", T("skills.formVersion"), current.Version),
			new("category", T("skills.formCategory"), current.Category),
			new("tags", T("skills.formTags"), string.Join(", ", current.Tags)),
			new("tools", T("skills.formTools"), string.Join(", ", current.Tools ?? [])),
			new("instructions", T("skills.formInstructions"), current.Instructions, true),
		], T("common.save"));
		if (form is null || string.IsNullOrWhiteSpace(form["name"]) || string.IsNullOrWhiteSpace(form["instructions"])) return;
		SkillRecord updated = current with
		{
			Name = form["name"].Trim(), Description = form["description"].Trim(), Author = form["author"].Trim(),
			Version = form["version"].Trim(), Category = form["category"].Trim(),
			Tags = Split(form["tags"]), Tools = Split(form["tools"]), Instructions = form["instructions"],
		};
		try { Operations.SaveSkill(updated); await RefreshAsync(); }
		catch (Exception exception) { ViewModel.ReportError(exception); }
	}

	private async Task ShowDetailsAsync(SkillSnapshot skill)
	{
		try
		{
			string json = Operations.ExportSkill(skill.Id);
			SkillRecord? record = JsonSerializer.Deserialize<SkillRecord>(json);
			await SettingsDialogs.ShowMessageAsync(Owner(), skill.Name, record?.Instructions ?? "(无指令正文)");
		}
		catch (Exception exception) { ViewModel.ReportError(exception); }
	}

	private StackPanel CardBody(string title, string subtitle)
	{
		StackPanel body = new() {Spacing = 8};
		Border card = new() {Background = Brush("#0D2232"), BorderBrush = Brush("#1D4053"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Child = body};
		_list.Children.Add(card);
		StackPanel heading = new() {Orientation = Orientation.Horizontal, Spacing = 8};
		heading.Children.Add(new TextBlock {Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brush("#E8F6FF")});
		heading.Children.Add(new TextBlock {Text = subtitle, FontSize = 11, Foreground = Brush("#718C9E"), VerticalAlignment = VerticalAlignment.Center});
		body.Children.Add(heading);
		return body;
	}

	private static bool Matches(string name, string description, IReadOnlyList<string> tags, string query, string category, string itemCategory)
	{
		bool categoryMatch = category == "all" || string.Equals(category, itemCategory, StringComparison.OrdinalIgnoreCase);
		bool queryMatch = query.Length == 0 || name.ToLowerInvariant().Contains(query) || description.ToLowerInvariant().Contains(query) || tags.Any(tag => tag.ToLowerInvariant().Contains(query));
		return categoryMatch && queryMatch;
	}

	private static IReadOnlyList<string> Split(string text) => text.Split([',', '，', ' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	private Window Owner() => (TopLevel.GetTopLevel(this) as Window)!;
}
