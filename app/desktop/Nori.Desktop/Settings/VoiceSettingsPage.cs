using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Nori.Desktop.Runtime;

namespace Nori.Desktop.Settings;

/// <summary>语音、TTS 与 STT 设置页。</summary>
public sealed class VoiceSettingsPage : SettingsPageBase
{
	private static readonly string[] ProviderKeys = ["openai", "custom", "gpt_sovits"];
	private ComboBox _provider = null!;
	private Slider _volume = null!;
	private Slider _speed = null!;
	private ToggleSwitch _autoPlay = null!;
	private StackPanel _providerFields = null!;
	private TextBox? _ttsBaseUrl;
	private TextBox? _ttsKey;
	private TextBox? _gptUrl;
	private TextBox? _refAudio;
	private TextBox? _promptText;
	private TextBox? _promptLang;
	private TextBox _ttsVoice = null!;
	private TextBox _sttBaseUrl = null!;
	private TextBox _sttKey = null!;
	private FAInfoBar _notice = null!;
	private VoiceSnapshot? _lastSnapshot;
	private bool _synchronizing;

	public VoiceSettingsPage(SettingsWindowViewModel viewModel) : base(viewModel)
	{
		Build();
	}

	public override string TitleKey => "voice.title";
	public override string SubtitleKey => "voice.subtitle";

	public override async Task RefreshAsync()
	{
		UiSnapshot snapshot = await ReadSnapshotAsync();
		_synchronizing = true;
		try
		{
			_lastSnapshot = snapshot.Voice;
			_volume.Value = snapshot.Voice.Volume * 100;
			_provider.SelectedIndex = Math.Max(0, Array.IndexOf(ProviderKeys, snapshot.Voice.TtsProvider));
			RebuildProviderFields(snapshot.Voice);
			_ttsVoice.Text = snapshot.Voice.TtsVoice;
			_speed.Value = snapshot.Voice.TtsSpeed;
			_autoPlay.IsChecked = snapshot.Voice.TtsAutoPlay;
			_sttBaseUrl.Text = snapshot.Voice.SttBaseUrl;
			_sttKey.PlaceholderText = snapshot.Voice.HasSttApiKey ? T("ai.saved") : "sk-…";
			_notice.IsOpen = snapshot.Voice.NoticePending;
		}
		finally
		{
			_synchronizing = false;
		}
	}

	private void Build()
	{
		StackPanel root = CreateRoot();
		StackPanel volumeCard = CreateCard(root, T("voice.volume"));
		Grid volumeRow = new() {ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12};
		_volume = new Slider {Minimum = 0, Maximum = 100, Value = 100, TickFrequency = 10};
		_volume.PropertyChanged += (_, args) =>
		{
			if (_synchronizing || args.Property != Slider.ValueProperty) return;
			Debounce("volume", () => RunBackground(() => Operations.UpdateVoice(new VoiceSettingsPatch {Volume = _volume.Value / 100})));
		};
		volumeRow.Children.Add(_volume);
		TextBlock volumeText = new() {Text = "100%", Width = 48, Foreground = Brush("#7DE3FF"), VerticalAlignment = VerticalAlignment.Center};
		_volume.PropertyChanged += (_, args) =>
		{
			if (args.Property == Slider.ValueProperty) volumeText.Text = $"{Math.Round(_volume.Value)}%";
		};
		Grid.SetColumn(volumeText, 1);
		volumeRow.Children.Add(volumeText);
		volumeCard.Children.Add(volumeRow);

		StackPanel ttsCard = CreateCard(root, T("voice.provider"));
		_provider = AddProvider(ttsCard);
		_providerFields = new StackPanel {Spacing = 10};
		ttsCard.Children.Add(_providerFields);
		_ttsVoice = AddTextField(ttsCard, T("voice.voice"), "nova", value => DebouncedVoice("voice", new VoiceSettingsPatch {TtsVoice = value.Trim()}), hint: "nova, alloy, shimmer…");
		Grid speedRow = new() {ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12};
		StackPanel speedField = new() {Spacing = 5};
		speedField.Children.Add(Label(T("voice.speed")));
		_speed = new Slider {Minimum = 0.5, Maximum = 2, Value = 1, TickFrequency = 0.1};
		_speed.PropertyChanged += (_, args) =>
		{
			if (_synchronizing || args.Property != Slider.ValueProperty) return;
			Debounce("speed", () => RunBackground(() => Operations.UpdateVoice(new VoiceSettingsPatch {TtsSpeed = _speed.Value})));
		};
		speedField.Children.Add(_speed);
		speedRow.Children.Add(speedField);
		TextBlock speedText = new() {Text = "1.0x", Width = 48, Foreground = Brush("#7DE3FF"), VerticalAlignment = VerticalAlignment.Center};
		_speed.PropertyChanged += (_, args) =>
		{
			if (args.Property == Slider.ValueProperty) speedText.Text = $"{_speed.Value:0.0}x";
		};
		Grid.SetColumn(speedText, 1);
		speedRow.Children.Add(speedText);
		ttsCard.Children.Add(speedRow);
		_autoPlay = AddSwitch(ttsCard, T("voice.autoPlay"), T("voice.autoPlayDesc"), true, value =>
		{
			if (!_synchronizing) RunBackground(() => Operations.UpdateVoice(new VoiceSettingsPatch {TtsAutoPlay = value}));
		});
		Button test = new() {Content = T("voice.test")};
		test.Classes.Add("settings-action");
		test.Classes.Add("accent");
		test.Click += async (_, _) =>
		{
			test.IsEnabled = false;
			try { await Operations.TestVoiceAsync(); } catch (Exception exception) { ViewModel.ReportError(exception); }
			finally { test.IsEnabled = true; }
		};
		ttsCard.Children.Add(test);

		_notice = new FAInfoBar
		{
			Title = T("voice.noticeTitle"),
			Message = T("voice.notice"),
			Severity = FAInfoBarSeverity.Warning,
			IsClosable = true,
			IsOpen = true,
		};
		_notice.Closed += (_, _) => RunBackground(Operations.AcknowledgeVoiceNotice);
		root.Children.Insert(0, _notice);

		StackPanel sttCard = CreateCard(root, T("voice.stt"), T("voice.sttHint"));
		_sttBaseUrl = AddTextField(sttCard, T("voice.sttBaseUrl"), "", value => DebouncedVoice("sttBase", new VoiceSettingsPatch {SttProvider = "whisper", SttBaseUrl = value.Trim()}), hint: "https://api.openai.com/v1");
		_sttKey = AddTextField(sttCard, T("voice.sttApiKey"), "", password: true, hint: "sk-…");
		_sttKey.LostFocus += (_, _) => SaveSecret(_sttKey, patch => new VoiceSettingsPatch {SttApiKey = patch});
	}

	private ComboBox AddProvider(StackPanel parent)
	{
		StackPanel field = new() {Spacing = 5};
		field.Children.Add(Label(T("voice.provider")));
		ComboBox combo = new()
		{
			ItemsSource = new[] {T("voice.openai"), T("voice.custom"), T("voice.gpt")},
			SelectedIndex = 0,
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
		};
		combo.SelectionChanged += (_, _) =>
		{
			if (_synchronizing || combo.SelectedIndex < 0) return;
			string provider = ProviderKeys[combo.SelectedIndex];
			RebuildProviderFields(_lastSnapshot ?? new VoiceSnapshot
			{
				TtsProvider = provider,
				TtsBaseUrl = "",
				TtsVoice = "",
				GptsovitsBaseUrl = "http://127.0.0.1:9880",
				GptsovitsRefAudio = "",
				GptsovitsPromptText = "",
				GptsovitsPromptLang = "zh",
				SttProvider = "whisper",
				SttBaseUrl = "",
			});
			RunBackground(() => Operations.UpdateVoice(new VoiceSettingsPatch {TtsProvider = provider}));
		};
		field.Children.Add(combo);
		parent.Children.Add(field);
		return combo;
	}

	private void RebuildProviderFields(VoiceSnapshot snapshot)
	{
		_providerFields.Children.Clear();
		_ttsBaseUrl = null;
		_ttsKey = null;
		_gptUrl = null;
		_refAudio = null;
		_promptText = null;
		_promptLang = null;
		if (_provider.SelectedIndex is 0 or 1)
		{
			_ttsBaseUrl = AddTextField(_providerFields, T("voice.baseUrl"), snapshot.TtsBaseUrl, value => DebouncedVoice("ttsBase", new VoiceSettingsPatch {TtsBaseUrl = value.Trim()}), hint: "https://api.openai.com/v1");
			_ttsKey = AddTextField(_providerFields, T("voice.apiKey"), "", password: true, hint: snapshot.HasTtsApiKey ? T("ai.saved") : "sk-…");
			_ttsKey.LostFocus += (_, _) => SaveSecret(_ttsKey, patch => new VoiceSettingsPatch {TtsApiKey = patch});
		}
		else
		{
			_gptUrl = AddTextField(_providerFields, T("voice.gptUrl"), snapshot.GptsovitsBaseUrl, value => DebouncedVoice("gptUrl", new VoiceSettingsPatch {GptsovitsBaseUrl = value.Trim()}), hint: "http://127.0.0.1:9880");
			_refAudio = AddTextField(_providerFields, T("voice.refAudio"), snapshot.GptsovitsRefAudio, value => DebouncedVoice("refAudio", new VoiceSettingsPatch {GptsovitsRefAudio = value.Trim()}));
			_promptText = AddTextField(_providerFields, T("voice.promptText"), snapshot.GptsovitsPromptText, value => DebouncedVoice("promptText", new VoiceSettingsPatch {GptsovitsPromptText = value.Trim()}));
			_promptLang = AddTextField(_providerFields, T("voice.promptLang"), snapshot.GptsovitsPromptLang, value => DebouncedVoice("promptLang", new VoiceSettingsPatch {GptsovitsPromptLang = value.Trim()}));
		}
	}

	private void DebouncedVoice(string key, VoiceSettingsPatch patch) => Debounce(key, () => RunBackground(() => Operations.UpdateVoice(patch)));

	private void SaveSecret(TextBox? box, Func<string, VoiceSettingsPatch> patchFactory)
	{
		string value = box?.Text?.Trim() ?? "";
		if (value.Length == 0) return;
		if (box is not null) box.Text = "";
		RunBackground(() => Operations.UpdateVoice(patchFactory(value)));
	}
}
