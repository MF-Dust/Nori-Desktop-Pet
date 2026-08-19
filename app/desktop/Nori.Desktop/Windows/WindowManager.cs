using Avalonia.Controls.ApplicationLifetimes;
using Nori.Core.Assets;
using Nori.Desktop.Bridge;

namespace Nori.Desktop.Windows;

/// <summary>
/// 窗口调度
///
/// 承接原来 Rust 侧 lib.rs setup / tray.rs 与前端 services/window/index.ts 的窗口调度职责.
/// 四个窗口在启动时一次性建好并全部隐藏, 之后只做显示/隐藏/关闭.
/// </summary>
public sealed class WindowManager(AssetServer assetServer, IClassicDesktopStyleApplicationLifetime lifetime)
{
	private readonly AssetServer _assetServer = assetServer;
	private readonly IClassicDesktopStyleApplicationLifetime _lifetime = lifetime;
	private readonly Dictionary<string, NoriWindow> _windows = [];

	/// <summary>
	/// 建好全部窗口 (不显示)
	/// </summary>
	public void CreateAll(NoriBridge bridge)
	{
		foreach (WindowDefinition definition in WindowDefinition.All)
		{
			NoriWindow window = new(definition, bridge, _assetServer.WindowUrl(definition.Label));
			// 关闭窗口不退出应用: 与 Tauri 版一致, 只有托盘退出与 exit_app 才结束进程
			window.Closing += (_, args) =>
			{
				if (window.AllowClose) return;
				args.Cancel = true;
				window.Hide();
			};
			_windows[definition.Label] = window;
		}
	}

	/// <summary>
	/// 按标签取窗口, 不存在返回 null
	/// </summary>
	public NoriWindow? Get(string? label) => label is not null && _windows.TryGetValue(label, out NoriWindow? window) ? window : null;

	/// <summary>
	/// 全部窗口
	/// </summary>
	public IEnumerable<NoriWindow> All => _windows.Values;

	/// <summary>
	/// 显示并聚焦窗口
	/// </summary>
	public void Show(string label)
	{
		if (Get(label) is not { } window) return;
		window.Show();
		window.Activate();
		if (label == WindowLabels.Pet) window.Topmost = true;
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
		window.AllowClose = true;
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
	/// 向所有窗口广播事件
	/// </summary>
	public void Broadcast(string name, object? payload)
	{
		foreach (NoriWindow window in _windows.Values) window.PostEvent(name, payload);
	}

	/// <summary>
	/// 退出应用
	/// </summary>
	public void Shutdown() => _lifetime.Shutdown(0);
}
