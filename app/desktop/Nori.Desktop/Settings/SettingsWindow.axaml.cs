using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Nori.Desktop.Bridge;
using Nori.Desktop.Runtime;
using Nori.Desktop.Windows;

namespace Nori.Desktop.Settings;

/// <summary>
/// 原生设置窗口。
///
/// 只负责窗口壳、页面懒加载和生命周期；配置业务由 SettingsOperations 完成。
/// </summary>
public partial class SettingsWindow : Window
{
	private readonly AppServices _services;
	private readonly SettingsWindowViewModel _viewModel;
	private bool _isRefreshing;

	/// <summary>窗口调度使用的隐藏而非销毁语义。</summary>
	public bool AllowClose { get; set; }

	/// <summary>隐藏窗口前提交所有页面的待写入字段。</summary>
	public void FlushPending() => _viewModel.FlushPending();

	/// <summary>供 Avalonia 设计器与资源加载器使用的无参构造函数。</summary>
	public SettingsWindow()
	{
		_services = null!;
		_viewModel = null!;
		InitializeComponent();
	}

	public SettingsWindow(WindowDefinition definition, AppServices services)
	{
		_services = services;
		Title = definition.Title;
		Width = definition.Width;
		Height = definition.Height;
		MinWidth = definition.MinWidth ?? 820;
		MinHeight = definition.MinHeight ?? 560;
		CanResize = definition.CanResize;
		ShowInTaskbar = definition.ShowInTaskbar;
		WindowStartupLocation = WindowStartupLocation.CenterScreen;
		Background = new SolidColorBrush(Color.Parse("#081724"));
		Icon = LoadIcon();
		
		if (OperatingSystem.IsWindows() && Environment.OSVersion.Version >= new Version(10, 0, 22000))
		{
			TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
			Background = Brushes.Transparent;
		}

		InitializeComponent();
		_viewModel = new SettingsWindowViewModel(services.Runtime ?? throw new InvalidOperationException("应用运行时尚未就绪"));
		DataContext = _viewModel;
		NavigationList.ItemsSource = _viewModel.Navigation;
		NavigationList.SelectedItem = _viewModel.SelectedItem;
		_viewModel.PropertyChanged += ViewModelOnPropertyChanged;
		_viewModel.NavigationChanged += RefreshNavigation;
		Closed += OnClosed;
		SettingsLocalization.Changed += OnLocalizationChanged;
	}

	/// <summary>窗口每次显示或重新激活时刷新当前页。</summary>
	public void RefreshOnShow() => _ = RefreshAsync();

	private void OnClosed(object? sender, EventArgs e)
	{
		SettingsLocalization.Changed -= OnLocalizationChanged;
		_viewModel.FlushPending();
	}

	private async void NavigationList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (NavigationList.SelectedItem is not SettingsNavigationItem item) return;
		_viewModel.SelectedItem = item;
		await ShowPageAsync(item.Key);
	}

	private void CloseButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Hide();

	private async Task RefreshAsync()
	{
		if (_isRefreshing || _services.Runtime is null) return;
		_isRefreshing = true;
		try
		{
			_viewModel.ClearError();
			UiSnapshot snapshot = await Task.Run(_viewModel.Operations.Snapshot);
			SettingsLocalization.SetLanguage(snapshot.General.Language);
			RefreshNavigation();
			SettingsNavigationItem item = _viewModel.SelectedItem ?? _viewModel.Navigation[0];
			await ShowPageAsync(item.Key);
		}
		catch (Exception exception)
		{
			_viewModel.ReportError(exception);
		}
		finally
		{
			_isRefreshing = false;
		}
	}

	private async Task ShowPageAsync(string key)
	{
		if (_services.Runtime is null) return;
		SettingsPageBase page = _viewModel.GetPage(key);
		PageContent.Content = page;
		PageTitle.Text = SettingsLocalization.Text(page.TitleKey);
		PageSubtitle.Text = SettingsLocalization.Text(page.SubtitleKey);
		await page.RefreshAsync();
	}

	private void RefreshNavigation()
	{
		NavigationList.ItemsSource = null;
		NavigationList.ItemsSource = _viewModel.Navigation;
		NavigationList.SelectedItem = _viewModel.SelectedItem;
		WindowTitle.Text = SettingsLocalization.Text("window.title");
		WindowSubtitle.Text = SettingsLocalization.Text("window.subtitle");
		CloseButton.Content = SettingsLocalization.Text("common.close");
		if (_viewModel.SelectedItem is { } item)
		{
			PageTitle.Text = SettingsLocalization.Text(_viewModel.GetPage(item.Key).TitleKey);
			PageSubtitle.Text = SettingsLocalization.Text(_viewModel.GetPage(item.Key).SubtitleKey);
		}
	}

	private void OnLocalizationChanged()
	{
		if (!Dispatcher.UIThread.CheckAccess())
		{
			Dispatcher.UIThread.Post(OnLocalizationChanged);
			return;
		}
		_viewModel.RebuildNavigation();
		RefreshNavigation();
		if (_viewModel.SelectedItem is { } item)
		{
			PageContent.Content = null;
			_viewModel.RecreatePage(item.Key);
			_ = ShowPageAsync(item.Key);
		}
	}

	private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(SettingsWindowViewModel.ErrorMessage))
		{
			ErrorText.Text = _viewModel.ErrorMessage;
			ErrorText.IsVisible = _viewModel.ErrorMessage.Length > 0;
		}
	}

	private static WindowIcon? LoadIcon()
	{
		try
		{
			return new WindowIcon(AssetLoader.Open(new Uri("avares://Nori.Desktop/Assets/icon.ico")));
		}
		catch (Exception exception) when (exception is FileNotFoundException or ArgumentException)
		{
			return null;
		}
	}
}
