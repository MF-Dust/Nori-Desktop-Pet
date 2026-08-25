using Microsoft.Playwright;

namespace Nori.Desktop.Automation.Browser;

/// <summary>使用已安装 Microsoft Edge 的隔离浏览器运行器。</summary>
public sealed class PlaywrightEdgeBrowserRunner : IAsyncDisposable
{
	private readonly object _gate = new();
	private IPlaywright? _playwright;
	private IBrowserContext? _context;
	private string? _profileDirectory;
	private int _disposed;

	/// <summary>当前是否已有独立 Edge 会话。</summary>
	public bool IsStarted
	{
		get
		{
			lock (_gate) return _context is not null;
		}
	}

	/// <summary>启动可见的隔离 Edge 会话；不会下载浏览器或复用默认 profile。</summary>
	public async Task<PlaywrightEdgeBrowserSession> StartAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Edge 浏览器自动化首发仅支持 Windows");
		lock (_gate)
		{
			if (_context is not null) throw new InvalidOperationException("浏览器自动化会话已经启动");
		}

		string profileDirectory = Path.Combine(Path.GetTempPath(), $"nori-edge-{Guid.NewGuid():N}");
		Directory.CreateDirectory(profileDirectory);
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			IPlaywright playwright = await Playwright.CreateAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
			IBrowserContext context = await playwright.Chromium.LaunchPersistentContextAsync(
				profileDirectory,
				new BrowserTypeLaunchPersistentContextOptions
				{
					Channel = "msedge",
					Headless = false,
					AcceptDownloads = false,
					HandleSIGINT = false,
					HandleSIGTERM = false,
					HandleSIGHUP = false,
				}).WaitAsync(cancellationToken).ConfigureAwait(false);
			context.SetDefaultTimeout(BrowserAutomationPolicy.DefaultTimeoutMilliseconds);
			context.Page += OnPage;

			lock (_gate)
			{
				_playwright = playwright;
				_context = context;
				_profileDirectory = profileDirectory;
			}

			IPage page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
			AttachPageHandlers(page);
			return new PlaywrightEdgeBrowserSession(this, context, page);
		}
		catch
		{
			await DisposeAsync().ConfigureAwait(false);
			TryDeleteDirectory(profileDirectory);
			throw;
		}
	}

	internal async ValueTask CloseSessionAsync(IBrowserContext context)
	{
		try
		{
			context.Page -= OnPage;
			await context.CloseAsync().ConfigureAwait(false);
		}
		catch (PlaywrightException)
		{
			// 浏览器已经崩溃或被用户关闭时仍继续清理临时 profile。
		}
		finally
		{
			IPlaywright? playwright;
			string? profile;
			lock (_gate)
			{
				if (ReferenceEquals(_context, context)) _context = null;
				playwright = _playwright;
				_playwright = null;
				profile = _profileDirectory;
				_profileDirectory = null;
			}
			playwright?.Dispose();
			if (profile is not null) TryDeleteDirectory(profile);
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		IBrowserContext? context;
		IPlaywright? playwright;
		string? profile;
		lock (_gate)
		{
			context = _context;
			_context = null;
			playwright = _playwright;
			_playwright = null;
			profile = _profileDirectory;
			_profileDirectory = null;
		}

		if (context is not null)
		{
			try { await context.CloseAsync().ConfigureAwait(false); }
			catch (PlaywrightException) { }
		}
		playwright?.Dispose();
		if (profile is not null) TryDeleteDirectory(profile);
	}

	private void OnPage(object? sender, IPage page) => AttachPageHandlers(page);

	private static void AttachPageHandlers(IPage page)
	{
		page.Dialog += OnDialog;
		page.FileChooser += OnFileChooser;
		page.Download += OnDownload;
	}

	private static void OnDialog(object? sender, IDialog dialog) => _ = DismissDialogAsync(dialog);

	private static async Task DismissDialogAsync(IDialog dialog)
	{
		try { await dialog.DismissAsync().ConfigureAwait(false); }
		catch (PlaywrightException) { }
	}

	private static void OnFileChooser(object? sender, IFileChooser chooser) => _ = CancelFileChooserAsync(chooser);

	private static async Task CancelFileChooserAsync(IFileChooser chooser)
	{
		try { await chooser.SetFilesAsync(Array.Empty<string>()).ConfigureAwait(false); }
		catch (PlaywrightException) { }
	}

	private static void OnDownload(object? sender, IDownload download) => _ = CancelDownloadAsync(download);

	private static async Task CancelDownloadAsync(IDownload download)
	{
		try { await download.CancelAsync().ConfigureAwait(false); }
		catch (PlaywrightException) { }
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
		}
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
	}
}

/// <summary>单个隔离 Edge 会话的受限操作面。</summary>
public sealed class PlaywrightEdgeBrowserSession : IAsyncDisposable
{
	private readonly PlaywrightEdgeBrowserRunner _owner;
	private readonly IBrowserContext _context;
	private readonly IPage _page;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private int _disposed;

	internal PlaywrightEdgeBrowserSession(PlaywrightEdgeBrowserRunner owner, IBrowserContext context, IPage page)
	{
		_owner = owner;
		_context = context;
		_page = page;
	}

	/// <summary>导航到用户确认过的 HTTP/HTTPS 地址。</summary>
	public async Task NavigateAsync(string url, CancellationToken cancellationToken = default)
	{
		BrowserAutomationPolicy.ValidateNavigation(url);
		await WithPageLockAsync(async page =>
		{
			await page.GotoAsync(url, new PageGotoOptions
			{
				WaitUntil = WaitUntilState.DOMContentLoaded,
				Timeout = BrowserAutomationPolicy.DefaultTimeoutMilliseconds,
			}).WaitAsync(cancellationToken).ConfigureAwait(false);
			await BrowserAutomationPolicy.EnsureSafePageAsync(page, cancellationToken).ConfigureAwait(false);
		}, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>读取页面可见文本摘要；返回值仅保留在调用方内存。</summary>
	public async Task<string> ReadVisibleTextAsync(CancellationToken cancellationToken = default) =>
		await WithPageLockAsync(async page =>
		{
			string text = await page.Locator("body").InnerTextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
			return text.Length <= BrowserAutomationPolicy.MaxVisibleTextCharacters
				? text
				: text[..BrowserAutomationPolicy.MaxVisibleTextCharacters];
		}, cancellationToken).ConfigureAwait(false);

	/// <summary>点击唯一可见的 CSS 元素。</summary>
	public async Task ClickAsync(string selector, CancellationToken cancellationToken = default)
	{
		string normalized = BrowserAutomationPolicy.ValidateSelector(selector);
		await WithPageLockAsync(async page =>
		{
			ILocator locator = page.Locator(normalized);
			if (await locator.CountAsync().WaitAsync(cancellationToken).ConfigureAwait(false) != 1
				|| !await locator.IsVisibleAsync().WaitAsync(cancellationToken).ConfigureAwait(false))
				throw new InvalidOperationException("浏览器目标不是唯一可见元素");
			await locator.ClickAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
			await BrowserAutomationPolicy.EnsureSafePageAsync(page, cancellationToken).ConfigureAwait(false);
		}, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>向唯一可见元素填入用户已确认的文本。</summary>
	public async Task FillAsync(string selector, string text, bool confirmed, CancellationToken cancellationToken = default)
	{
		string normalizedSelector = BrowserAutomationPolicy.ValidateSelector(selector);
		string normalizedText = BrowserAutomationPolicy.ValidateInput(text, confirmed);
		await WithPageLockAsync(async page =>
		{
			ILocator locator = page.Locator(normalizedSelector);
			if (await locator.CountAsync().WaitAsync(cancellationToken).ConfigureAwait(false) != 1
				|| !await locator.IsVisibleAsync().WaitAsync(cancellationToken).ConfigureAwait(false))
				throw new InvalidOperationException("浏览器输入目标不是唯一可见元素");
			await locator.FillAsync(normalizedText).WaitAsync(cancellationToken).ConfigureAwait(false);
			await BrowserAutomationPolicy.EnsureSafePageAsync(page, cancellationToken).ConfigureAwait(false);
		}, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>在当前页面滚动有限距离。</summary>
	public async Task ScrollAsync(int pixels, CancellationToken cancellationToken = default)
	{
		int amount = BrowserAutomationPolicy.ValidateScroll(pixels);
		await WithPageLockAsync(async page =>
		{
			await page.Mouse.WheelAsync(0, amount).WaitAsync(cancellationToken).ConfigureAwait(false);
		}, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>等待页面稳定，不执行脚本。</summary>
	public async Task WaitAsync(int milliseconds, CancellationToken cancellationToken = default) =>
		await Task.Delay(BrowserAutomationPolicy.ValidateWait(milliseconds), cancellationToken).ConfigureAwait(false);

	/// <summary>获取受大小限制的内存截图，不写入文件。</summary>
	public async Task<byte[]> ScreenshotAsync(CancellationToken cancellationToken = default) =>
		await WithPageLockAsync(async page =>
		{
			byte[] data = await page.ScreenshotAsync(new PageScreenshotOptions
			{
				Type = ScreenshotType.Png,
				FullPage = false,
			}).WaitAsync(cancellationToken).ConfigureAwait(false);
			return BrowserAutomationPolicy.ValidateScreenshot(data);
		}, cancellationToken).ConfigureAwait(false);

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		_gate.Dispose();
		await _owner.CloseSessionAsync(_context).ConfigureAwait(false);
	}

	private async Task<T> WithPageLockAsync<T>(Func<IPage, Task<T>> operation, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try { return await operation(_page).ConfigureAwait(false); }
		finally { _gate.Release(); }
	}

	private async Task WithPageLockAsync(Func<IPage, Task> operation, CancellationToken cancellationToken)
	{
		await WithPageLockAsync(async page =>
		{
			await operation(page).ConfigureAwait(false);
			return true;
		}, cancellationToken).ConfigureAwait(false);
	}
}
