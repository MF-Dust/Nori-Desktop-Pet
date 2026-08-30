namespace Nori.PluginRuntime;

/// <summary>
/// 插件失败诊断装配: 给 PluginException 附加脱敏的安全字段。
///
/// 只含插件 ID/版本、宿主版本、根因异常类型、TypeLoad 类型名与程序集文件名;
/// 不上传完整安装路径、用户目录与插件私有配置。
/// </summary>
internal static class PluginDiagnostics
{
	public static PluginException Attach(
		PluginException exception,
		string? pluginId,
		string? pluginVersion,
		string hostApiVersion,
		string hostVersion)
	{
		try
		{
			Exception? root = exception;
			for (Exception? inner = exception.InnerException; inner is not null; inner = inner.InnerException) root = inner;
			exception.DiagnosticExceptionType = root.GetType().FullName;
			if (root is TypeLoadException typeLoad && !string.IsNullOrWhiteSpace(typeLoad.TypeName))
			{
				string typeName = typeLoad.TypeName.Trim().Replace('\r', ' ').Replace('\n', ' ');
				exception.DiagnosticTypeLoadTypeName = typeName.Length <= 200 ? typeName : typeName[..200];
			}
			if (root is FileLoadException fileLoad && !string.IsNullOrWhiteSpace(fileLoad.FileName))
				exception.DiagnosticAssemblyName = Path.GetFileName(fileLoad.FileName);
			exception.DiagnosticPluginId = pluginId;
			exception.DiagnosticPluginVersion = pluginVersion;
			exception.DiagnosticHostApiVersion = hostApiVersion;
			exception.DiagnosticHostVersion = hostVersion;
		}
		catch
		{
			// 诊断装配失败不影响失败语义。
		}
		return exception;
	}
}
