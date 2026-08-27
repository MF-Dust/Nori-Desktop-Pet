using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using Nori.Core.Logging;
using Nori.Plugin.Abstractions;

namespace Nori.Desktop.Plugins;

/// <summary>
/// 动态插件窗口管理器
///
/// 独立于 WindowDefinition.All 与 WindowManager 的四个固定主窗口,
/// 专职负责插件 Web 视图窗口的生命周期调度、标签命名空间维护与安全标识校验.
/// </summary>
public sealed partial class PluginWindowHost : IAsyncDisposable
{
	private readonly ConcurrentDictionary<string, PluginWebViewWindow> _windows = new(StringComparer.Ordinal);
	private readonly FileLogger? _logger;
	private int _disposed;

	[GeneratedRegex(@"^[a-zA-Z0-9_\-\.]{1,64}$", RegexOptions.Compiled)]
	private static partial Regex SafeIdPattern();

	[GeneratedRegex(@"^[a-zA-Z0-9_\-\.]{1,128}$", RegexOptions.Compiled)]
	private static partial Regex SafePluginIdPattern();

	public PluginWindowHost(FileLogger? logger = null)
	{
		_logger = logger;
	}

	/// <summary>当前存活的插件窗口集合</summary>
	public IReadOnlyCollection<PluginWebViewWindow> ActiveWindows => _windows.Values.ToArray();

	/// <summary>
	/// 校验插件 ID 或窗口 ID 是否符合安全命名规范 (防路径遍历与非法字符注入)
	/// </summary>
	public static bool IsValidPluginId(string? id) =>
		!string.IsNullOrWhiteSpace(id) && id.Length <= 128 && !id.Contains('/') && !id.Contains('\\') && !id.Contains(':') && !id.Contains("..") && SafePluginIdPattern().IsMatch(id);

	public static bool IsValidId(string? id)
	{
		if (string.IsNullOrWhiteSpace(id)) return false;
		if (id.Length > 64) return false;
		if (id.Contains('/') || id.Contains('\\') || id.Contains(':') || id.Contains("..")) return false;
		return SafeIdPattern().IsMatch(id);
	}

	/// <summary>
	/// 断言 ID 有效性，不合法时抛出领域异常
	/// </summary>
	public static void ValidateId(string id, string paramName)
	{
		if (!IsValidId(id))
			throw new ArgumentException($"标识符 '{id}' 不合法: 仅允许 1-64 位字母、数字、下划线、短横线与点，且不得包含路径或冒号字符。", paramName);
	}

	/// <summary>校验插件 ID。manifest 负责更严格的规范 ID 校验，窗口标签允许最多 128 位。</summary>
	public static void ValidatePluginId(string id, string paramName)
	{
		if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Contains('/') || id.Contains('\\') || id.Contains(':') || id.Contains("..") || !SafePluginIdPattern().IsMatch(id))
			throw new ArgumentException($"插件标识符 '{id}' 不合法。", paramName);
	}

	/// <summary>
	/// 生成标准的插件窗口全局标签
	/// </summary>
	public static string BuildLabel(string pluginId, string windowId)
	{
		ValidatePluginId(pluginId, nameof(pluginId));
		ValidateId(windowId, nameof(windowId));
		return $"plugin:{pluginId}:{windowId}";
	}

	/// <summary>
	/// 从窗口全局标签解析所属插件 ID 与窗口 ID
	/// </summary>
	public static bool TryParseLabel(
		string? label,
		[NotNullWhen(true)] out string? pluginId,
		[NotNullWhen(true)] out string? windowId)
	{
		pluginId = null;
		windowId = null;

		if (string.IsNullOrWhiteSpace(label)) return false;
		if (!label.StartsWith("plugin:", StringComparison.Ordinal)) return false;

		string[] parts = label.Split(':');
		if (parts.Length != 3) return false;

		if (!IsValidPluginId(parts[1]) || !IsValidId(parts[2])) return false;
		pluginId = parts[1];
		windowId = parts[2];
		return true;
	}

	/// <summary>
	/// 创建并登记一个插件 Web 视图窗口 (在 UI 线程执行实例化)
	/// </summary>
	public async Task<IPluginWebViewWindow> CreateWindowAsync(
		PluginDescriptorSummary descriptor,
		PluginWebViewOptions options,
		CancellationToken revocationToken = default,
		CancellationToken cancellationToken = default)
	{
		if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(PluginWindowHost));
		ArgumentNullException.ThrowIfNull(descriptor);
		ArgumentNullException.ThrowIfNull(options);

		ValidatePluginId(descriptor.Id, nameof(descriptor.Id));
		ValidateId(options.Id, nameof(options.Id));
		if (string.IsNullOrWhiteSpace(options.Title) || options.Title.Any(char.IsControl)) throw new ArgumentException("插件窗口标题无效", nameof(options));
		if (string.IsNullOrWhiteSpace(options.EntryPoint) || options.EntryPoint.Any(char.IsControl)) throw new ArgumentException("插件窗口入口无效", nameof(options));
		if (double.IsNaN(options.Width) || double.IsInfinity(options.Width) || options.Width <= 0 || double.IsNaN(options.Height) || double.IsInfinity(options.Height) || options.Height <= 0) throw new ArgumentException("插件窗口尺寸无效", nameof(options));

		string label = BuildLabel(descriptor.Id, options.Id);

		if (_windows.TryGetValue(label, out PluginWebViewWindow? existing))
		{
			_logger?.Write(LogSource.Backend, "info", $"插件窗口 '{label}' 已存在，重新激活");
			await existing.ShowAsync(cancellationToken).ConfigureAwait(false);
			return existing;
		}

		cancellationToken.ThrowIfCancellationRequested();

		PluginWebViewWindow window = await Dispatcher.UIThread.InvokeAsync(() =>
		{
			return new PluginWebViewWindow(descriptor, options, revocationToken: revocationToken);
		});

		window.WindowClosed += OnWindowClosed;

		_windows[label] = window;
		_logger?.Write(LogSource.Backend, "info", $"已创建并注册插件窗口: {label}");

		return window;
	}

	/// <summary>
	/// 按全局标签获取活动插件窗口
	/// </summary>
	public PluginWebViewWindow? GetWindow(string? label)
	{
		if (string.IsNullOrWhiteSpace(label)) return null;
		return _windows.TryGetValue(label, out PluginWebViewWindow? window) ? window : null;
	}

	/// <summary>
	/// 按插件 ID 和窗口 ID 获取活动插件窗口
	/// </summary>
	public PluginWebViewWindow? GetWindow(string pluginId, string windowId)
	{
		if (!IsValidPluginId(pluginId) || !IsValidId(windowId)) return null;
		string label = BuildLabel(pluginId, windowId);
		return GetWindow(label);
	}

	/// <summary>
	/// 获取指定插件名下的所有活动窗口
	/// </summary>
	public IReadOnlyList<PluginWebViewWindow> GetWindowsForPlugin(string pluginId)
	{
		if (!IsValidPluginId(pluginId)) return [];
		return _windows.Values.Where(w => string.Equals(w.PluginId, pluginId, StringComparison.Ordinal)).ToArray();
	}

	/// <summary>
	/// 关闭并注销指定插件窗口
	/// </summary>
	public async Task<bool> CloseWindowAsync(string label, CancellationToken cancellationToken = default)
	{
		if (!_windows.TryGetValue(label, out PluginWebViewWindow? window))
		{
			return false;
		}

		await window.CloseAsync(cancellationToken).ConfigureAwait(false);
		_windows.TryRemove(label, out _);
		return true;
	}

	/// <summary>
	/// 关闭指定插件名下的全部窗口 (例如插件卸载或上下文租约撤销)
	/// </summary>
	public async Task CloseAllWindowsForPluginAsync(string pluginId, CancellationToken cancellationToken = default)
	{
		IReadOnlyList<PluginWebViewWindow> pluginWindows = GetWindowsForPlugin(pluginId);
		foreach (PluginWebViewWindow window in pluginWindows)
		{
			try
			{
				await window.CloseAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger?.Write(LogSource.Backend, "warn", $"关闭插件窗口 [{window.Label}] 发生异常: {ex.Message}");
			}
			finally
			{
				_windows.TryRemove(window.Label, out _);
			}
		}
	}

	/// <summary>
	/// 关闭并清空所有存活的插件窗口
	/// </summary>
	public async Task CloseAllAsync(CancellationToken cancellationToken = default)
	{
		PluginWebViewWindow[] windows = _windows.Values.ToArray();
		_windows.Clear();

		foreach (PluginWebViewWindow window in windows)
		{
			try
			{
				window.WindowClosed -= OnWindowClosed;
				await window.CloseAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger?.Write(LogSource.Backend, "warn", $"关闭插件窗口 [{window.Label}] 发生异常: {ex.Message}");
			}
		}
	}

	private void OnWindowClosed(PluginWebViewWindow window)
	{
		window.WindowClosed -= OnWindowClosed;
		if (_windows.TryRemove(window.Label, out _))
		{
			_logger?.Write(LogSource.Backend, "info", $"插件窗口已注销: {window.Label}");
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		await CloseAllAsync().ConfigureAwait(false);
	}
}
