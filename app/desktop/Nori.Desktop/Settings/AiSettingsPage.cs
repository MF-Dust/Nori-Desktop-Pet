using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nori.Desktop.Runtime;

namespace Nori.Desktop.Settings;

/// <summary>AI 大脑设置页。</summary>
public sealed class AiSettingsPage : SettingsPageBase
{
	private static readonly string[] ProviderKeys = ["openai", "openai_responses", "anthropic", "google"];
	private static readonly string[] ProviderNames = ["OpenAI (Chat Completions)", "OpenAI (Responses)", "Anthropic (Messages)", "Google (GenAI / Gemini)"];
	private static readonly Dictionary<string, string> DefaultBaseUrls = new()
	{
		["openai"] = "https://api.openai.com/v1",
		["openai_responses"] = "https://api.openai.com/v1",
		["anthropic"] = "https://api.anthropic.com/v1",
		["google"] = "https://generativelanguage.googleapis.com/v1beta",
	};

	private TextBox _baseUrl = null!;
	private TextBox _apiKey = null!;
	private ComboBox _provider = null!;
	private ComboBox _model = null!;
	private TextBox _persona = null!;
	private TextBlock _error = null!;
	private bool _synchronizing;

	public AiSettingsPage(SettingsWindowViewModel viewModel) : base(viewModel)
	{
		Build();
	}

	public override string TitleKey => "ai.title";
	public override string SubtitleKey => "ai.subtitle";

	public override async Task RefreshAsync()
	{
		UiSnapshot snapshot = await ReadSnapshotAsync();
		_synchronizing = true;
		try
		{
			_provider.SelectedIndex = Math.Max(0, Array.IndexOf(ProviderKeys, snapshot.Ai.Provider));
			_baseUrl.Text = snapshot.Ai.BaseUrl;
			_model.ItemsSource = string.IsNullOrWhiteSpace(snapshot.Ai.Model) ? Array.Empty<string>() : new[] {snapshot.Ai.Model};
			_model.SelectedIndex = string.IsNullOrWhiteSpace(snapshot.Ai.Model) ? -1 : 0;
			_persona.Text = snapshot.Ai.Persona;
			_apiKey.PlaceholderText = snapshot.Ai.HasApiKey ? T("ai.saved") : "sk-…";
		}
		finally
		{
			_synchronizing = false;
		}
	}

	private void Build()
	{
		StackPanel root = CreateRoot();
		StackPanel brain = CreateCard(root, T("ai.title"), T("ai.subtitle"));
		_provider = AddProvider(brain);
		_baseUrl = AddTextField(brain, T("ai.baseUrl"), "", value =>
		{
			if (_synchronizing) return;
			Debounce("baseUrl", () => RunBackground(() => Operations.UpdateAi(new AiSettingsPatch {BaseUrl = value.Trim()})));
		}, hint: "https://api.openai.com/v1");
		_apiKey = AddTextField(brain, T("ai.apiKey"), "", password: true, hint: "sk-…");
		_apiKey.LostFocus += (_, _) =>
		{
			string value = _apiKey.Text?.Trim() ?? "";
			if (value.Length == 0) return;
			_apiKey.Text = "";
			RunBackground(() => Operations.UpdateAi(new AiSettingsPatch {ApiKey = value}));
		};
		
		Grid modelRow = new() {ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8};
		StackPanel modelField = new() {Spacing = 5};
		modelField.Children.Add(Label(T("ai.model")));
		_model = new ComboBox {HorizontalContentAlignment = HorizontalAlignment.Stretch};
		_model.SelectionChanged += (_, _) =>
		{
			if (_synchronizing || _model.SelectedItem is not string model || model.Length == 0) return;
			RunBackground(() => Operations.UpdateAi(new AiSettingsPatch {Model = model}));
		};
		modelField.Children.Add(_model);
		modelRow.Children.Add(modelField);
		Button fetch = new() {Content = T("ai.fetch"), VerticalAlignment = VerticalAlignment.Bottom};
		fetch.Classes.Add("settings-action");
		fetch.Classes.Add("accent");
		fetch.Click += async (_, _) => await FetchModelsAsync(fetch);
		Grid.SetColumn(fetch, 1);
		modelRow.Children.Add(fetch);
		brain.Children.Add(modelRow);

		_persona = AddTextField(brain, T("ai.persona"), "", value =>
		{
			if (_synchronizing) return;
			Debounce("persona", () => RunBackground(() => Operations.UpdateAi(new AiSettingsPatch {Persona = value})));
		}, multiline: true, hint: T("ai.personaHint"));
		_error = new TextBlock {Foreground = Brush("#FF9BA7"), TextWrapping = TextWrapping.Wrap, IsVisible = false};
		brain.Children.Add(_error);
	}

	private ComboBox AddProvider(StackPanel parent)
	{
		StackPanel field = new() {Spacing = 5};
		field.Children.Add(Label(T("ai.provider")));
		ComboBox combo = new() {ItemsSource = ProviderNames, SelectedIndex = 0, HorizontalContentAlignment = HorizontalAlignment.Stretch};
		combo.SelectionChanged += (_, _) =>
		{
			if (_synchronizing || combo.SelectedIndex < 0) return;
			string provider = ProviderKeys[combo.SelectedIndex];
			if (string.IsNullOrWhiteSpace(_baseUrl.Text) || DefaultBaseUrls.Values.Contains(_baseUrl.Text))
			{
				_baseUrl.Text = DefaultBaseUrls[provider];
			}
			_model.ItemsSource = Array.Empty<string>();
			_model.SelectedIndex = -1;
			RunBackground(() => Operations.UpdateAi(new AiSettingsPatch {Provider = provider, BaseUrl = _baseUrl.Text?.Trim() ?? ""}));
		};
		field.Children.Add(combo);
		parent.Children.Add(field);
		return combo;
	}

	private async Task FetchModelsAsync(Button button)
	{
		if (_provider.SelectedIndex < 0 || string.IsNullOrWhiteSpace(_baseUrl.Text))
		{
			_error.Text = T("ai.baseUrlError");
			_error.IsVisible = true;
			return;
		}
		button.IsEnabled = false;
		button.Content = T("ai.fetching");
		_error.IsVisible = false;
		try
		{
			string? key = _apiKey.Text?.Trim();
			IReadOnlyList<string> models = await Operations.FetchModelsAsync(
				ProviderKeys[_provider.SelectedIndex], _baseUrl.Text.Trim(), key);
			_model.ItemsSource = models;
			if (models.Count > 0)
			{
				_model.SelectedIndex = 0;
				RunBackground(() => Operations.UpdateAi(new AiSettingsPatch {Model = models[0]}));
			}
			if (!string.IsNullOrEmpty(key))
			{
				_apiKey.Text = "";
				RunBackground(() => Operations.UpdateAi(new AiSettingsPatch {ApiKey = key}));
			}
			if (models.Count == 0)
			{
				_error.Text = "接口未返回可用模型";
				_error.IsVisible = true;
			}
		}
		catch (Exception exception)
		{
			_error.Text = exception.Message;
			_error.IsVisible = true;
		}
		finally
		{
			button.IsEnabled = true;
			button.Content = T("ai.fetch");
		}
	}
}
