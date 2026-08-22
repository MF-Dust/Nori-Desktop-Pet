using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Nori.Core.Logging;
using Nori.Desktop.Bridge;
using Nori.Desktop.Windows;

namespace Nori.Desktop.Tray;

/// <summary>
/// 系统托盘
///
/// 对应 Rust 版 tray.rs. 托盘是唯一常驻的入口: 左键开主界面, 菜单切换桌宠与退出.
/// </summary>
public static class TrayMenu
{
	/// <summary>
	/// 挂上托盘图标与菜单
	/// </summary>
	public static void Install(Application application, AppServices services)
	{
		services.Logger.Write(LogSource.Backend, "info", "初始化托盘菜单");

		NativeMenuItem openMain = new("打开主界面");
		openMain.Click += (_, _) => ShowMain(services);

		NativeMenuItem togglePet = new("显示/隐藏桌宠");
		togglePet.Click += (_, _) =>
		{
			services.Logger.Write(LogSource.Backend, "info", "托盘菜单：切换桌宠显示");
			services.Windows.TogglePet();
		};

		NativeMenuItem openSettings = new("打开设置");
		openSettings.Click += (_, _) =>
		{
			services.Logger.Write(LogSource.Backend, "info", "托盘菜单：打开设置");
			services.Windows.Show(WindowLabels.Settings);
		};

		NativeMenuItem quit = new("退出应用");
		quit.Click += (_, _) =>
		{
			services.Logger.Write(LogSource.Backend, "info", "托盘菜单：退出应用");
			services.Windows.Shutdown();
		};

		TrayIcon tray = new()
		{
			Icon = LoadIcon(),
			ToolTipText = "Nori Desktop Pet - 点击打开主界面",
			Menu = [openMain, togglePet, openSettings, quit],
		};
		// 左键点击直接开主界面, 不弹菜单
		tray.Clicked += (_, _) => ShowMain(services);

		TrayIcon.SetIcons(application, [tray]);
		services.Logger.Write(LogSource.Backend, "info", "托盘菜单初始化完成");
	}

	/// <summary>
	/// 显示主窗口
	/// </summary>
	private static void ShowMain(AppServices services)
	{
		services.Logger.Write(LogSource.Backend, "info", "托盘操作：已显示主窗口");
		services.Windows.Show(WindowLabels.Main);
	}

	/// <summary>
	/// 托盘图标, 缺失时返回 null (托盘会退化成无图标但仍可用)
	/// </summary>
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
