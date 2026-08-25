using System.Net;

namespace Nori.Desktop.Automation.Browser;

/// <summary>浏览器自动化的固定安全边界。</summary>
public static class BrowserAutomationPolicy
{
	public const int MaxUrlCharacters = 2048;
	public const int MaxSelectorCharacters = 256;
	public const int MaxInputCharacters = 4096;
	public const int MaxVisibleTextCharacters = 32_000;
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

	/// <summary>文本输入必须由用户显式确认，且不允许空白外的超长内容。</summary>
	public static string ValidateInput(string value, bool confirmed)
	{
		if (!confirmed) throw new InvalidOperationException("浏览器文本输入需要用户确认");
		if (value.Length > MaxInputCharacters) throw new InvalidOperationException("浏览器输入文本超过长度限制");
		return value;
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

	internal static async Task EnsureSafePageAsync(Microsoft.Playwright.IPage page, CancellationToken cancellationToken)
	{
		string[] sensitiveSelectors =
		[
			"input[type='password']",
			"input[autocomplete='cc-number']",
			"input[autocomplete='cc-csc']",
			"input[name*='captcha' i]",
			"iframe[src*='captcha' i]",
			"iframe[src*='recaptcha' i]",
			"[data-sitekey]",
		];

		foreach (string selector in sensitiveSelectors)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (await page.Locator(selector).CountAsync().WaitAsync(cancellationToken).ConfigureAwait(false) > 0)
				throw new PausedException(PauseReason.SensitivePage, "浏览器页面包含登录、支付或验证码交互，需要用户接管");
		}
	}
}
