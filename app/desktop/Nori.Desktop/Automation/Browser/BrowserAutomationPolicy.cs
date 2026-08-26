using System.Net;
using Nori.Core.Automation;

namespace Nori.Desktop.Automation.Browser;

/// <summary>浏览器自动化的固定安全边界。</summary>
public static class BrowserAutomationPolicy
{
	public const int MaxUrlCharacters = 2048;
	public const int MaxSelectorCharacters = 256;
	public const int MaxInputCharacters = 4096;
	/// <summary>受限结果的最大 UTF-8 字节数。</summary>
	public const int MaxVisibleTextBytes = BrowserAutomationTaskLimits.MaxVisibleTextBytes;
	/// <summary>旧字符上限名称的兼容别名；实际由 UTF-8 字节上限约束。</summary>
	public const int MaxVisibleTextCharacters = MaxVisibleTextBytes;
	public const int MaxScreenshotBytes = 4 * 1024 * 1024;
	public const int MaxScrollPixels = 2_000;
	public const int MaxWaitMilliseconds = 30_000;
	public const int DefaultTimeoutMilliseconds = 10_000;

	/// <summary>只允许带主机的 HTTP/HTTPS 地址，不允许凭据或外部协议。</summary>
	public static Uri ValidateNavigation(string value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > MaxUrlCharacters)
			throw new InvalidOperationException("浏览器地址为空或超过长度限制");
		if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
			|| uri is null
			|| (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
			|| string.IsNullOrWhiteSpace(uri.Host)
			|| !string.IsNullOrEmpty(uri.UserInfo))
			throw new InvalidOperationException("浏览器只允许不含凭据的 HTTP 或 HTTPS 地址");
		return uri;
	}

	/// <summary>限制为短 CSS 选择器；执行器不会把它当作脚本执行。</summary>
	public static string ValidateSelector(string value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > MaxSelectorCharacters || value.Contains('\n') || value.Contains('\r'))
			throw new InvalidOperationException("浏览器选择器为空或超过长度限制");
		return value.Trim();
	}

	/// <summary>验证输入长度；结构化任务的审批由 AppRuntime 持有，绝不相信客户端 confirmed 字段。</summary>
	public static string ValidateInput(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (value.Length > MaxInputCharacters) throw new InvalidOperationException("浏览器输入文本超过长度限制");
		return value;
	}

	/// <summary>兼容旧的直接会话调用；结构化任务不使用客户端确认标志。</summary>
	public static string ValidateInput(string value, bool confirmed)
	{
		if (!confirmed) throw new InvalidOperationException("浏览器文本输入需要用户确认");
		return ValidateInput(value);
	}

	/// <summary>限制滚动幅度，避免模型生成不可控的长距离操作。</summary>
	public static int ValidateScroll(int pixels)
	{
		if (pixels is < -MaxScrollPixels or > MaxScrollPixels || pixels == 0)
			throw new InvalidOperationException("浏览器滚动幅度无效");
		return pixels;
	}

	/// <summary>限制单次等待，取消由调用方的 CancellationToken 控制。</summary>
	public static int ValidateWait(int milliseconds)
	{
		if (milliseconds is < 1 or > MaxWaitMilliseconds)
			throw new InvalidOperationException("浏览器等待时间超过限制");
		return milliseconds;
	}

	/// <summary>按安全策略校验一个已经解析的结构化动作。</summary>
	public static BrowserAutomationAction ValidateAction(BrowserAutomationAction action)
	{
		ArgumentNullException.ThrowIfNull(action);
		switch (action)
		{
			case BrowserNavigateAction navigate:
				ValidateNavigation(navigate.Url);
				break;
			case BrowserClickAction click:
				ValidateSelector(click.Selector);
				break;
			case BrowserFillAction fill:
				ValidateSelector(fill.Selector);
				ValidateInput(fill.Text);
				break;
			case BrowserScrollAction scroll:
				ValidateScroll(scroll.Pixels);
				break;
			case BrowserWaitAction wait:
				ValidateWait(wait.Milliseconds);
				break;
			case BrowserReadVisibleTextAction:
				break;
			default:
				throw new InvalidOperationException("浏览器动作类型不在白名单内");
		}
		return action;
	}

	/// <summary>校验整个结构化计划；解析器和此策略共同拒绝未授权能力。</summary>
	public static BrowserAutomationTaskPlan ValidatePlan(BrowserAutomationTaskPlan plan)
	{
		ArgumentNullException.ThrowIfNull(plan);
		if (plan.Actions.Count is < 1 or > BrowserAutomationTaskLimits.MaxActions)
			throw new InvalidOperationException("浏览器任务动作数超过限制");
		foreach (BrowserAutomationAction action in plan.Actions) ValidateAction(action);
		return plan;
	}

	/// <summary>验证内存截图大小，截图不落盘。</summary>
	public static byte[] ValidateScreenshot(byte[] data)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (data.Length == 0 || data.Length > MaxScreenshotBytes)
			throw new InvalidOperationException("浏览器截图为空或超过大小限制");
		return data;
	}

	/// <summary>浏览器页面需要用户接管时使用的稳定原因。</summary>
	public enum PauseReason
	{
		SensitivePage,
		FileChooser,
		Download,
		PermissionDialog,
	}

	/// <summary>页面包含敏感交互时暂停自动化。</summary>
	public sealed class PausedException : InvalidOperationException
	{
		public PauseReason Reason { get; }

		public PausedException(PauseReason reason, string message) : base(message)
		{
			Reason = reason;
		}
	}

	internal static Task EnsureSafePageAsync(Microsoft.Playwright.IPage page, CancellationToken cancellationToken) =>
		EnsureSafePageAsync(page, null, cancellationToken);

	/// <summary>在 DOM 动作前后重新检查受保护页面与浏览器安全信号。</summary>
	internal static async Task EnsureSafePageAsync(
		Microsoft.Playwright.IPage page,
		BrowserSafetySignals? safetySignals,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(page);
		safetySignals?.ThrowIfPaused();
		string[] sensitiveSelectors =
		[
			"input[type='password']",
			"input[autocomplete*='password' i]",
			"input[name*='password' i]",
			"input[name*='passcode' i]",
			"input[autocomplete*='cc-' i]",
			"input[name*='card' i]",
			"input[name*='payment' i]",
			"input[name*='cvv' i]",
			"input[name*='captcha' i]",
			"iframe[src*='captcha' i]",
			"iframe[src*='recaptcha' i]",
			"[data-sitekey]",
			"input[type='file']",
			"[data-permission]",
			"[aria-label*='permission' i]",
		];

		foreach (string selector in sensitiveSelectors)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (await page.Locator(selector).CountAsync().WaitAsync(cancellationToken).ConfigureAwait(false) > 0)
				throw new PausedException(PauseReason.SensitivePage, "浏览器页面包含登录、支付、验证码、文件或权限交互，需要用户接管");
		}
		safetySignals?.ThrowIfPaused();
	}
}

/// <summary>Page 事件触发的 fail-closed 安全信号；不保存事件正文。</summary>
internal sealed class BrowserSafetySignals
{
	private int _reason = -1;

	/// <summary>记录第一个安全信号。</summary>
	public void Report(BrowserAutomationPolicy.PauseReason reason) =>
		Interlocked.CompareExchange(ref _reason, (int)reason, -1);

	/// <summary>若会话已触发安全信号则停止任务。</summary>
	public void ThrowIfPaused()
	{
		int value = Volatile.Read(ref _reason);
		if (value < 0) return;
		BrowserAutomationPolicy.PauseReason reason = (BrowserAutomationPolicy.PauseReason)value;
		throw new BrowserAutomationPolicy.PausedException(reason, "浏览器会话触发受保护交互，需要用户接管");
	}
}
