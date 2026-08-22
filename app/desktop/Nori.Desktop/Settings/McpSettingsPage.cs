using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nori.Core.Mcp;
using Nori.Desktop.Runtime;

namespace Nori.Desktop.Settings;

/// <summary>MCP 服务与内置工具设置页。</summary>
public sealed class McpSettingsPage : SettingsPageBase
{
	private Button _serverTab = null!;
	private Button _toolTab = null!;
	private StackPanel _list = null!;
	private TextBox _search = null!;
	private IReadOnlyList<McpServerStatusInfo> _servers = [];
	private IReadOnlyList<ToolSnapshot> _tools = [];
	private bool _toolMode;

	public McpSettingsPage(SettingsWindowViewModel viewModel) : base(viewModel)
	{
		Build();
	}

	public override string TitleKey => "mcp.title";
	public override string SubtitleKey => "mcp.subtitle";

	public override async Task RefreshAsync()
	{
		try
		{
			_servers = await Operations.GetMcpServersAsync();
			UiSnapshot snapshot = await ReadSnapshotAsync();
			_tools = snapshot.Tools.Where(tool => string.Equals(tool.Category, "builtin", StringComparison.OrdinalIgnoreCase)).ToArray();
			Rebuild();
		}
		catch (Exception exception) { ViewModel.ReportError(exception); }
	}

	private void Build()
	{
		StackPanel root = CreateRoot();
		Grid toolbar = new() {ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), ColumnSpacing = 8};
		_serverTab = new Button {Content = T("mcp.servers")};
		_toolTab = new Button {Content = T("mcp.tools")};
		_serverTab.Click += (_, _) => { _toolMode = false; Rebuild(); };
		_toolTab.Click += (_, _) => { _toolMode = true; Rebuild(); };
		toolbar.Children.Add(_serverTab);
		Grid.SetColumn(_toolTab, 1);
		toolbar.Children.Add(_toolTab);
		_search = new TextBox {PlaceholderText = T("mcp.search")};
		_search.TextChanged += (_, _) => Rebuild();
		Grid.SetColumn(_search, 2);
		toolbar.Children.Add(_search);
		Button import = new() {Content = T("mcp.import")};
		import.Click += async (_, _) => await ImportAsync();
		Grid.SetColumn(import, 3);
		toolbar.Children.Add(import);
		Button add = new() {Content = T("mcp.add")};
		add.Classes.Add("accent");
		add.Click += async (_, _) => await EditServerAsync(null);
		Grid.SetColumn(add, 4);
		toolbar.Children.Add(add);
		root.Children.Add(toolbar);
		_list = new StackPanel {Spacing = 8};
		root.Children.Add(_list);
	}

	private void Rebuild()
	{
		if (_list is null) return;
		_serverTab.Classes.Set("accent", !_toolMode);
		_toolTab.Classes.Set("accent", _toolMode);
		_list.Children.Clear();
		string query = _search.Text?.Trim().ToLowerInvariant() ?? "";
		if (_toolMode)
		{
			foreach (ToolSnapshot tool in _tools.Where(tool => query.Length == 0 || tool.Name.ToLowerInvariant().Contains(query) || tool.Description.ToLowerInvariant().Contains(query))) AddTool(tool);
		}
		else
		{
			foreach (McpServerStatusInfo server in _servers.Where(server => query.Length == 0 || server.Name.ToLowerInvariant().Contains(query) || server.ServerId.ToLowerInvariant().Contains(query))) AddServer(server);
		}
		if (_list.Children.Count == 0) _list.Children.Add(Hint(_toolMode ? T("mcp.noTools") : T("mcp.noServers")));
	}

	private void AddServer(McpServerStatusInfo server)
	{
		StackPanel body = CardBody(server.Name, $"{server.ServerId} · {server.Status}");
		if (!string.IsNullOrWhiteSpace(server.ErrorMessage)) body.Children.Add(new TextBlock {Text = server.ErrorMessage, Foreground = Brush("#FF9BA7"), TextWrapping = TextWrapping.Wrap});
		if (server.Tools.Count > 0)
		{
			body.Children.Add(new TextBlock {Text = $"{T("mcp.toolCount")}: {string.Join(", ", server.Tools.Select(tool => tool.Name))}", Foreground = Brush("#7DE3FF"), TextWrapping = TextWrapping.Wrap});
		}
		StackPanel actions = new() {Orientation = Orientation.Horizontal, Spacing = 6};
		Button connect = new() {Content = server.Status == "connected" ? T("mcp.disconnect") : T("mcp.connect")};
		connect.Click += async (_, _) =>
		{
			connect.IsEnabled = false;
			try
			{
				if (server.Status == "connected") await Operations.DisconnectMcpServerAsync(server.ServerId);
				else await Operations.ConnectMcpServerAsync(server.ServerId);
				await RefreshAsync();
			}
			catch (Exception exception) { ViewModel.ReportError(exception); connect.IsEnabled = true; }
		};
		actions.Children.Add(connect);
		if (server.Tools.Count > 0)
		{
			Button testTool = new() {Content = T("mcp.testTool")};
			testTool.Click += async (_, _) => await TestMcpToolAsync(server);
			actions.Children.Add(testTool);
		}
		Button delete = new() {Content = T("mcp.delete")};
		delete.Classes.Add("danger");
		delete.Click += async (_, _) =>
		{
			if (!await SettingsDialogs.ConfirmAsync(Owner(), T("mcp.delete"), T("mcp.deleteConfirm").Replace("{0}", server.Name, StringComparison.Ordinal), true)) return;
			await Operations.DeleteMcpServerAsync(server.ServerId);
			await RefreshAsync();
		};
		actions.Children.Add(delete);
		body.Children.Add(actions);
	}

	private void AddTool(ToolSnapshot tool)
	{
		StackPanel body = CardBody(tool.Name, tool.PermissionLevel);
		body.Children.Add(new TextBlock {Text = tool.Description, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#A9C0CE"), FontSize = 12});
		Grid row = new() {ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8};
		Button test = new() {Content = T("mcp.test"), IsEnabled = tool.Enabled && tool.PermissionLevel == "safe"};
		test.Click += async (_, _) => await TestBuiltinToolAsync(tool);
		row.Children.Add(test);
		ToggleSwitch enabled = new() {IsChecked = tool.Enabled, OnContent = T("mcp.enabled"), OffContent = T("mcp.disabled")};
		enabled.IsCheckedChanged += (_, _) => RunBackground(() => Operations.ToggleTool(tool.Name, enabled.IsChecked == true));
		Grid.SetColumn(enabled, 1);
		row.Children.Add(enabled);
		body.Children.Add(row);
	}

	private async Task EditServerAsync(McpServerStatusInfo? existing)
	{
		IReadOnlyDictionary<string, string>? form = await SettingsDialogs.FormAsync(Owner(), T("mcp.add"),
		[
			new("id", T("mcp.formId"), existing?.ServerId ?? $"mcp_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}"),
			new("name", T("mcp.formName"), existing?.Name ?? T("mcp.newName")),
			new("transport", T("mcp.formTransport"), "stdio"),
			new("command", T("mcp.formCommand"), "npx"),
			new("args", T("mcp.formArgs"), ""),
			new("env", T("mcp.formEnv"), "", true),
			new("url", T("mcp.formUrl"), "", false),
		], T("common.save"));
		if (form is null || string.IsNullOrWhiteSpace(form["name"])) return;
		string transport = form["transport"].Trim().ToLowerInvariant() == "sse" ? McpTransportType.Sse : McpTransportType.Stdio;
		McpServerConfig config = new()
		{
			Id = form["id"].Trim(),
			Name = form["name"].Trim(),
			Transport = transport,
			Command = form["command"].Trim(),
			Args = form["args"].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
			Env = ParseEnv(form["env"]),
			Url = string.IsNullOrWhiteSpace(form["url"]) ? null : form["url"].Trim(),
			Enabled = true,
			AutoConnect = true,
		};
		try
		{
			McpServerStatusInfo test = await Operations.TestMcpServerAsync(config);
			if (test.Status == "error")
			{
				if (!await SettingsDialogs.ConfirmAsync(Owner(), T("mcp.testFailed"), test.ErrorMessage ?? T("mcp.notReady"))) return;
			}
			await Operations.SaveMcpServerAsync(config);
			await RefreshAsync();
		}
		catch (Exception exception) { ViewModel.ReportError(exception); }
	}

	private async Task ImportAsync()
	{
		IReadOnlyDictionary<string, string>? form = await SettingsDialogs.FormAsync(Owner(), T("mcp.import"), [new("url", T("mcp.importUrl"), "https://…/mcp.json")], T("mcp.import"));
		string? url = form?["url"];
		if (string.IsNullOrWhiteSpace(url)) return;
		try
		{
			await Operations.ImportMcpUrlAsync(url.Trim());
			await RefreshAsync();
		}
		catch (Exception exception) { ViewModel.ReportError(exception); }
	}

	private async Task TestBuiltinToolAsync(ToolSnapshot tool)
	{
		IReadOnlyDictionary<string, string>? form = await SettingsDialogs.FormAsync(Owner(), $"{T("mcp.testTool")}: {tool.Name}", [new("arguments", T("mcp.testJson"), "{}", true)], T("mcp.execute"));
		if (form is null) return;
		try
		{
			JsonNode? args = JsonNode.Parse(form["arguments"]);
			object? result = await Operations.ExecuteSafeToolAsync(tool.Name, args);
			await SettingsDialogs.ShowMessageAsync(Owner(), tool.Name, JsonSerializer.Serialize(result, new JsonSerializerOptions {WriteIndented = true}));
		}
		catch (Exception exception) { ViewModel.ReportError(exception); }
	}

	private async Task TestMcpToolAsync(McpServerStatusInfo server)
	{
		McpToolDefinition tool = server.Tools[0];
		IReadOnlyDictionary<string, string>? form = await SettingsDialogs.FormAsync(Owner(), $"{T("mcp.testTool")}: {tool.Name}", [new("arguments", T("mcp.testJson"), "{}", true)], T("mcp.execute"));
		if (form is null) return;
		try
		{
			JsonObject? args = JsonNode.Parse(form["arguments"]) as JsonObject;
			object? result = await Operations.CallMcpToolAsync(server.ServerId, tool.Name, args);
			await SettingsDialogs.ShowMessageAsync(Owner(), tool.Name, result?.ToString() ?? T("mcp.emptyResult"));
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

	private static Dictionary<string, string> ParseEnv(string text)
	{
		Dictionary<string, string> result = [];
		foreach (string line in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
		{
			int index = line.IndexOf('=');
			if (index > 0) result[line[..index].Trim()] = line[(index + 1)..].Trim();
		}
		return result;
	}

	private Window Owner() => (TopLevel.GetTopLevel(this) as Window)!;
}
