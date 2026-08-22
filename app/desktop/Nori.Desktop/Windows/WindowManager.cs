using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Nori.Core.Assets;
using Nori.Desktop.Bridge;

namespace Nori.Desktop.Windows;

/// <summary>
/// 窗口调度
///
/// 承接原来 Rust 侧 lib.rs setup / tray.rs 与前端 services/window/index.ts 的窗口调度职责.
/// 包含三个 WebView2 窗口 (first-run, init, main) 与一个原生 OpenGL 桌宠窗口 (pet)。
/// </summary>
public sealed class WindowManager(AssetServer assetServer, IClassicDesktopStyleApplicationLifetime lifetime) : IWindowManager
{
	private readonly AssetServer _assetServer = assetServer;
	private readonly IClassicDesktopStyleApplicationLifetime _lifetime = lifetime;
	private readonly Dictionary<string, Window> _windows = [];
	private PetWindow? _petWindow;

	/// <summary>
	/// 建好全部窗口 (不显示)
	/// </summary>
	public void CreateAll(NoriBridge bridge, AppServices services)
	{
		foreach (WindowDefinition definition in WindowDefinition.All)
		{
			if (definition.Label == WindowLabels.Pet)
			{
				PetWindow petWindow = new(definition, services);
				petWindow.Closing += (_, args) =>
				{
					if (petWindow.AllowClose) return;
					args.Cancel = true;
					petWindow.Hide();
				};
				_windows[definition.Label] = petWindow;
				_petWindow = petWindow;
			}
			else
			{
				NoriWindow window = new(definition, bridge, _assetServer.WindowUrl(definition.Label));
				window.Closing += (_, args) =>
				{
					if (window.AllowClose) return;
					args.Cancel = true;
					window.Hide();
				};
				_windows[definition.Label] = window;
			}
		}
	}

	/// <summary>
	/// 按标签取窗口, 不存在返回 null
	/// </summary>
	public Window? Get(string? label) => label is not null && _windows.TryGetValue(label, out Window? window) ? window : null;

	/// <summary>
	/// 按标签取 WebView2 窗口
	/// </summary>
	public NoriWindow? GetNoriWindow(string? label) => Get(label) as NoriWindow;

	/// <summary>
	/// 原生桌宠窗口引用
	/// </summary>
	public PetWindow? Pet => _petWindow;

	/// <summary>
	/// 全部窗口
	/// </summary>
	public IEnumerable<Window> All => _windows.Values;

	/// <summary>
	/// 显示并聚焦窗口
	/// </summary>
	public void Show(string label)
	{
		if (Get(label) is not { } window) return;
		window.Show();
		window.Activate();
		if (window is PetWindow pet)
		{
			pet.Topmost = true;
			pet.ApplyWindowSize();
		}
	}

	/// <summary>
	/// 隐藏窗口
	/// </summary>
	public void Hide(string label) => Get(label)?.Hide();

	/// <summary>
	/// 关闭窗口 (真正销毁, 不再复用)
	/// </summary>
	public void Close(string label)
	{
		if (Get(label) is not { } window) return;
		_windows.Remove(label);
		if (window is NoriWindow nw) nw.AllowClose = true;
		else if (window is PetWindow pw) pw.AllowClose = true;
		window.Close();
	}

	/// <summary>
	/// 切换桌宠显示状态
	/// </summary>
	public void TogglePet()
	{
		if (Get(WindowLabels.Pet) is not { } pet) return;
		if (pet.IsVisible) pet.Hide();
		else Show(WindowLabels.Pet);
	}

	/// <summary>
	/// 向所有 WebView2 窗口广播事件
	/// </summary>
	public void Broadcast(string name, object? payload)
	{
		foreach (Window window in _windows.Values)
		{
			if (window is NoriWindow noriWindow)
			{
				noriWindow.PostEvent(name, payload);
			}
		}
	}

	/// <summary>
	/// 退出应用
	/// </summary>
	public void Shutdown() => _lifetime.Shutdown(0);
}
