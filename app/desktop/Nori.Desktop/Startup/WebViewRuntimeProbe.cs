namespace Nori.Desktop.Startup;

/// <summary>宿主 WebView 运行时类型。</summary>
internal enum WebViewAdapterType
{
	WebView2,
	WkWebView,
	WebKitGtk,
}

/// <summary>WebView 运行时探测结果。</summary>
internal sealed record DetailedWebViewAdapterInfo(bool IsInstalled, string Version, string UnavailableReason);

/// <summary>只做平台运行时存在性探测，不创建 WebView。</summary>
internal static class WebViewAdapterInfo
{
	public static DetailedWebViewAdapterInfo GetAdapterInfo(WebViewAdapterType adapterType)
	{
		return adapterType switch
		{
			WebViewAdapterType.WebView2 => ProbeWebView2(),
			WebViewAdapterType.WkWebView => new DetailedWebViewAdapterInfo(true, "系统 WebKit", ""),
			WebViewAdapterType.WebKitGtk => new DetailedWebViewAdapterInfo(true, "系统 WebKitGTK", ""),
			_ => new DetailedWebViewAdapterInfo(false, "", "未知 WebView 类型"),
		};
	}

	private static DetailedWebViewAdapterInfo ProbeWebView2()
	{
		string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
		string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
		string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string[] roots = [
			Path.Combine(programFiles, "Microsoft", "EdgeWebView", "Application"),
			Path.Combine(programFilesX86, "Microsoft", "EdgeWebView", "Application"),
			Path.Combine(localAppData, "Microsoft", "EdgeWebView", "Application"),
		];
		string? installed = roots.FirstOrDefault(Directory.Exists);
		return installed is null
			? new DetailedWebViewAdapterInfo(false, "", "未找到 Microsoft Edge WebView2 Evergreen Runtime")
			: new DetailedWebViewAdapterInfo(true, "已安装", "");
	}
}
