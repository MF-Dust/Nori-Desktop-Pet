using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
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
	private readonly Dictionary<string, FANavigationViewItem> _navigationItems = [];
	private bool _isRefreshing;
	private bool _suppressNavigation;

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
		_viewModel = new SettingsWindowViewModel(services.Runtime ?? throw new InvalidOperationException("应用运行时尚未就绪"));
		Title = definition.Title;
		Width = definition.Width;
		Height = definition.Height;
		MinWidth = definition.MinWidth ?? 820;
		MinHeight = definition.MinHeight ?? 560;
		CanResize = definition.CanResize;
		ShowInTaskbar = definition.ShowInTaskbar;
		WindowStartupLocation = WindowStartupLocation.CenterScreen;
		Icon = LoadIcon();
		
		if (OperatingSystem.IsWindows() && Environment.OSVersion.Version >= new Version(10, 0, 22000))
		{
			TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
			Background = Brushes.Transparent;
		}

		InitializeComponent();
		PageContent.PageTransition = new CrossFade(TimeSpan.FromMilliseconds(180));
		DataContext = _viewModel;
		_viewModel.PropertyChanged += ViewModelOnPropertyChanged;
		_viewModel.NavigationChanged += RefreshNavigation;
		Closed += OnClosed;
		SettingsLocalization.Changed += OnLocalizationChanged;
		RefreshNavigation();
	}

	/// <summary>窗口每次显示或重新激活时刷新当前页。</summary>
	public void RefreshOnShow() => _ = RefreshAsync();

	private void OnClosed(object? sender, EventArgs e)
	{
		SettingsLocalization.Changed -= OnLocalizationChanged;
		_viewModel.NavigationChanged -= RefreshNavigation;
		_viewModel.FlushPending();
	}

	private async void NavigationView_OnItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
	{
		if (_suppressNavigation) return;
		if (GetNavigationItem(e) is not { } item) return;
		try
		{
			_viewModel.SelectedItem = item;
			await ShowPageAsync(item.Key);
		}
		catch (Exception exception)
		{
			_viewModel.ReportError(exception);
		}
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
			_viewModel.RebuildNavigation();
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
		try
		{
			SettingsPageBase page = _viewModel.GetPage(key);
			PageContent.Content = page;
			PageTitle.Text = SettingsLocalization.Text(page.TitleKey);
			PageSubtitle.Text = SettingsLocalization.Text(page.SubtitleKey);
			await page.RefreshAsync();
		}
		catch (Exception exception)
		{
			_viewModel.ReportError(exception);
		}
	}

	private void RefreshNavigation()
	{
		if (!Dispatcher.UIThread.CheckAccess())
		{
			Dispatcher.UIThread.Post(RefreshNavigation);
			return;
		}

		_suppressNavigation = true;
		try
		{
			_navigationItems.Clear();
			NavigationView.MenuItems.Clear();
			foreach (SettingsNavigationGroup group in _viewModel.Groups)
			{
				FANavigationViewItem groupItem = new()
				{
					Content = group.Title,
					IconSource = group.IconSource,
					IsExpanded = true,
					SelectsOnInvoked = false,
				};
				foreach (SettingsNavigationItem item in group.Items)
				{
					FANavigationViewItem pageItem = new()
					{
						Content = item.Title,
						Tag = item,
						IconSource = item.IconSource,
					};
					groupItem.MenuItems.Add(pageItem);
					_navigationItems[item.Key] = pageItem;
				}
				NavigationView.MenuItems.Add(groupItem);
			}

			if (_viewModel.SelectedItem is { } selected && _navigationItems.TryGetValue(selected.Key, out FANavigationViewItem? selectedItem))
			{
				NavigationView.SelectedItem = selectedItem;
			}
			WindowTitle.Text = SettingsLocalization.Text("window.title");
			if (_viewModel.SelectedItem is { } selectedPage)
			{
				SettingsPageBase page = _viewModel.GetPage(selectedPage.Key);
				PageTitle.Text = SettingsLocalization.Text(page.TitleKey);
				PageSubtitle.Text = SettingsLocalization.Text(page.SubtitleKey);
			}
		}
		finally
		{
			_suppressNavigation = false;
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
		if (_viewModel.SelectedItem is { } item && PageContent.Content is SettingsPageBase)
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

	private static SettingsNavigationItem? GetNavigationItem(FANavigationViewItemInvokedEventArgs args)
	{
		return args.InvokedItemContainer switch
		{
			FANavigationViewItem {Tag: SettingsNavigationItem item} => item,
			_ when args.InvokedItem is SettingsNavigationItem item => item,
			_ => null,
		};
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
