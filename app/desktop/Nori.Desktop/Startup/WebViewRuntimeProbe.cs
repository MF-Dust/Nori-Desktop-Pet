using System.Runtime.InteropServices;
using Microsoft.Win32;

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
			WebViewAdapterType.WkWebView => ProbeWkWebView(),
			WebViewAdapterType.WebKitGtk => ProbeWebKitGtk(),
			_ => new DetailedWebViewAdapterInfo(false, "", "未知 WebView 类型"),
		};
	}

	private static DetailedWebViewAdapterInfo ProbeWkWebView()
	{
		if (!OperatingSystem.IsMacOS()) return new DetailedWebViewAdapterInfo(false, "", "WKWebView 仅支持 macOS");
		return NativeLibrary.TryLoad("/System/Library/Frameworks/WebKit.framework/WebKit", out nint handle)
			? ReleaseLoaded(handle, new DetailedWebViewAdapterInfo(true, "系统 WebKit", ""))
			: new DetailedWebViewAdapterInfo(false, "", "未找到系统 WebKit.framework");
	}

	private static DetailedWebViewAdapterInfo ProbeWebKitGtk()
	{
		if (!OperatingSystem.IsLinux()) return new DetailedWebViewAdapterInfo(false, "", "WebKitGTK 仅支持 Linux");
		string[] names = ["libwebkit2gtk-4.1.so.0", "libwebkit2gtk-4.0.so.37", "libwebkit2gtk-4.0.so"];
		foreach (string name in names)
			if (NativeLibrary.TryLoad(name, out nint handle)) return ReleaseLoaded(handle, new DetailedWebViewAdapterInfo(true, name, ""));
		return new DetailedWebViewAdapterInfo(false, "", "未找到 WebKitGTK 4.x 运行时");
	}

	private static DetailedWebViewAdapterInfo ReleaseLoaded(nint handle, DetailedWebViewAdapterInfo result)
	{
		NativeLibrary.Free(handle);
		return result;
	}

	private static DetailedWebViewAdapterInfo ProbeWebView2()
	{
		if (!OperatingSystem.IsWindows()) return new DetailedWebViewAdapterInfo(false, "", "WebView2 仅支持 Windows");
		const string clientKey = @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
		foreach (RegistryView view in new[] {RegistryView.Registry64, RegistryView.Registry32})
		{
			try
			{
				using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
				using RegistryKey? key = machine.OpenSubKey(clientKey);
				if (key?.GetValue("pv") is string version && Version.TryParse(version, out _))
					return new DetailedWebViewAdapterInfo(true, version, "");
				using RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
				using RegistryKey? userKey = user.OpenSubKey(clientKey);
				if (userKey?.GetValue("pv") is string userVersion && Version.TryParse(userVersion, out _))
					return new DetailedWebViewAdapterInfo(true, userVersion, "");
			}
			catch (System.Security.SecurityException) { }
			catch (UnauthorizedAccessException) { }
		}
		return new DetailedWebViewAdapterInfo(false, "", "未找到 Microsoft Edge WebView2 Evergreen Runtime 注册信息");
	}
}
