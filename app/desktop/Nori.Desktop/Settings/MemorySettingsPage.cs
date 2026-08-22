using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Nori.Core.Memory;
using Nori.Desktop.Runtime;

namespace Nori.Desktop.Settings;

/// <summary>长期记忆与 Embedding 设置页。</summary>
public sealed class MemorySettingsPage : SettingsPageBase
{
	private TextBox _embeddingModel = null!;
	private TextBox _embeddingBase = null!;
	private TextBox _embeddingKey = null!;
	private TextBox _embeddingDimensions = null!;
	private TextBox _newContent = null!;
	private TextBox _newTags = null!;
	private Slider _importance = null!;
	private TextBox _search = null!;
	private StackPanel _memoryList = null!;
	private TextBlock _status = null!;
	private bool _synchronizing;

	public MemorySettingsPage(SettingsWindowViewModel viewModel) : base(viewModel)
	{
		Build();
	}

	public override string TitleKey => "memory.title";
	public override string SubtitleKey => "memory.subtitle";

	public override async Task RefreshAsync()
	{
		UiSnapshot snapshot = await ReadSnapshotAsync();
		_synchronizing = true;
		try
		{
			_embeddingModel.Text = snapshot.Embedding.Model;
			_embeddingBase.Text = snapshot.Embedding.BaseUrl;
			_embeddingDimensions.Text = snapshot.Embedding.Dimensions;
			_embeddingKey.PlaceholderText = snapshot.Embedding.HasApiKey ? T("ai.saved") : "sk-…";
		}
		finally
		{
			_synchronizing = false;
		}
		await LoadMemoriesAsync();
	}

	private void Build()
	{
		StackPanel root = CreateRoot();
		StackPanel embedding = CreateCard(root, T("memory.embedding"));
		_embeddingModel = AddTextField(embedding, T("memory.model"), "BAAI/bge-m3", value =>
		{
			if (!_synchronizing) Debounce("embeddingModel", () => RunBackground(() => Operations.UpdateEmbedding(new EmbeddingSettingsPatch {Model = value.Trim()})));
		});
		_embeddingBase = AddTextField(embedding, T("memory.baseUrl"), "", value =>
		{
			if (!_synchronizing) Debounce("embeddingBase", () => RunBackground(() => Operations.UpdateEmbedding(new EmbeddingSettingsPatch {BaseUrl = value.Trim()})));
		}, hint: "留空复用 AI 大脑配置 / Reuse AI settings");
		_embeddingKey = AddTextField(embedding, T("memory.apiKey"), "", password: true, hint: "sk-…");
		_embeddingKey.LostFocus += (_, _) =>
		{
			string value = _embeddingKey.Text?.Trim() ?? "";
			if (value.Length == 0) return;
			_embeddingKey.Text = "";
			RunBackground(() => Operations.UpdateEmbedding(new EmbeddingSettingsPatch {ApiKey = value}));
		};
		_embeddingDimensions = AddTextField(embedding, T("memory.dimensions"), "", value =>
		{
			if (!_synchronizing) Debounce("embeddingDimensions", () => RunBackground(() => Operations.UpdateEmbedding(new EmbeddingSettingsPatch {Dimensions = value.Trim()})));
		}, hint: "留空使用模型默认维数");
		Button reembed = new() {Content = T("memory.reembed")};
		reembed.Classes.Add("accent");
		reembed.Click += async (_, _) =>
		{
			reembed.IsEnabled = false;
			_status.Text = "正在计算向量…";
			try
			{
				int count = await Operations.ReembedMemoriesAsync();
				_status.Text = $"已为 {count} 条记忆生成向量索引";
			}
			catch (Exception exception) { _status.Text = exception.Message; }
			finally { reembed.IsEnabled = true; }
		};
		embedding.Children.Add(reembed);
		_status = new TextBlock {Foreground = Brush("#7DE3FF"), TextWrapping = TextWrapping.Wrap};
		embedding.Children.Add(_status);

		StackPanel add = CreateCard(root, T("memory.add"));
		_newContent = AddTextField(add, T("memory.content"), "", multiline: true, hint: T("memory.contentHint"));
		_newTags = AddTextField(add, T("memory.tags"), "", hint: T("memory.tagsHint"));
		Grid addMeta = new() {ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12};
		StackPanel importanceField = new() {Spacing = 5};
		TextBlock importanceLabel = Label($"{T("memory.importance")}: 80%");
		importanceField.Children.Add(importanceLabel);
		_importance = new Slider {Minimum = 0.1, Maximum = 1, Value = 0.8, TickFrequency = 0.1};
		_importance.PropertyChanged += (_, args) =>
		{
			if (args.Property == Slider.ValueProperty) importanceLabel.Text = $"{T("memory.importance")}: {Math.Round(_importance.Value * 100)}%";
		};
		importanceField.Children.Add(_importance);
		addMeta.Children.Add(importanceField);
		Button save = new() {Content = T("memory.save")};
		save.Classes.Add("accent");
		save.Click += async (_, _) =>
		{
			if (string.IsNullOrWhiteSpace(_newContent.Text)) return;
			save.IsEnabled = false;
			try
			{
				await Operations.AddMemoryAsync(_newContent.Text.Trim(), _importance.Value, _newTags.Text?.Trim());
				_newContent.Text = "";
				_newTags.Text = "";
				await LoadMemoriesAsync();
			}
			catch (Exception exception) { ViewModel.ReportError(exception); }
			finally { save.IsEnabled = true; }
		};
		Grid.SetColumn(save, 1);
		addMeta.Children.Add(save);
		add.Children.Add(addMeta);

		StackPanel listCard = CreateCard(root, T("memory.list"));
		Grid searchRow = new() {ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8};
		_search = new TextBox {PlaceholderText = T("memory.search")};
		_search.KeyDown += async (_, args) =>
		{
			if (args.Key == Avalonia.Input.Key.Enter) await LoadMemoriesAsync();
		};
		searchRow.Children.Add(_search);
		Button search = new() {Content = T("common.refresh")};
		search.Click += async (_, _) => await LoadMemoriesAsync();
		Grid.SetColumn(search, 1);
		searchRow.Children.Add(search);
		listCard.Children.Add(searchRow);
		Button clear = new() {Content = T("memory.clear")};
		clear.Classes.Add("danger");
		clear.HorizontalAlignment = HorizontalAlignment.Left;
		clear.Click += async (_, _) =>
		{
			if (!await SettingsDialogs.ConfirmAsync(Owner(), T("memory.clear"), T("memory.clearConfirm"), true)) return;
			await Task.Run(Operations.ClearMemories);
			await LoadMemoriesAsync();
		};
		listCard.Children.Add(clear);
		_memoryList = new StackPanel {Spacing = 6};
		listCard.Children.Add(_memoryList);
	}

	private async Task LoadMemoriesAsync()
	{
		try
		{
			IReadOnlyList<MemoryItem> items;
			string keyword = _search?.Text?.Trim() ?? "";
			items = keyword.Length > 0 ? await Operations.SearchMemoriesAsync(keyword, 50) : await Task.Run(() => Operations.ListMemories(50));
			RebuildMemoryList(items);
		}
		catch (Exception exception)
		{
			ViewModel.ReportError(exception);
		}
	}

	private void RebuildMemoryList(IReadOnlyList<MemoryItem> items)
	{
		_memoryList.Children.Clear();
		if (items.Count == 0)
		{
			_memoryList.Children.Add(Hint(T("memory.empty")));
			return;
		}
		foreach (MemoryItem item in items)
		{
			Grid row = new() {ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 3)};
			StackPanel text = new() {Spacing = 4};
			string tags = string.IsNullOrWhiteSpace(item.Tags) ? "" : $"[{item.Tags}] ";
			text.Children.Add(new TextBlock {Text = $"{tags}{T("memory.importance")} {Math.Round(item.Importance * 100)}%", FontSize = 11, Foreground = Brush("#7DE3FF")});
			text.Children.Add(new TextBlock {Text = item.Content, FontSize = 13, Foreground = Brush("#E8F6FF"), TextWrapping = TextWrapping.Wrap});
			text.Children.Add(new TextBlock {Text = item.CreatedAt, FontSize = 11, Foreground = Brush("#718C9E")});
			row.Children.Add(text);
			Button delete = new()
			{
				Content = new FASymbolIcon {Symbol = FASymbol.Delete, FontSize = 14},
				Width = 36,
			};
			ToolTip.SetTip(delete, T("memory.delete"));
			delete.Classes.Add("danger");
			delete.Click += async (_, _) =>
			{
				if (!await SettingsDialogs.ConfirmAsync(Owner(), T("memory.delete"), T("memory.deleteConfirm"), true)) return;
				await Task.Run(() => Operations.DeleteMemory(item.Id));
				await LoadMemoriesAsync();
			};
			Grid.SetColumn(delete, 1);
			row.Children.Add(delete);
			_memoryList.Children.Add(row);
			_memoryList.Children.Add(Separator());
		}
	}

	private Window Owner() => (TopLevel.GetTopLevel(this) as Window)!;
}
