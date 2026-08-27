using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Nori.Plugin.Abstractions;
using Nori.Plugin.Arcade.Abstractions;
using Nori.Plugin.Games.Abstractions;
using Nori.Plugin.Harness.Abstractions;

namespace Nori.Plugin.Runtime;

/// <summary>插件程序集隔离上下文。它不是安全沙箱，插件仍是受信任进程内代码。</summary>
public sealed class PluginLoadContext : AssemblyLoadContext
{
	private readonly AssemblyDependencyResolver _resolver;
	private static readonly HashSet<string> ContractAssemblies =
	[
		typeof(INoriPlugin).Assembly.GetName().Name!,
		typeof(IGameRegistry).Assembly.GetName().Name!,
		typeof(IArcadeRegistry).Assembly.GetName().Name!,
		typeof(IPluginHarness).Assembly.GetName().Name!,
	];

	public PluginLoadContext(string mainAssemblyPath) : base($"Nori.Plugin:{Path.GetFileNameWithoutExtension(mainAssemblyPath)}", isCollectible: true)
	{
		_resolver = new AssemblyDependencyResolver(mainAssemblyPath);
	}

	protected override Assembly? Load(AssemblyName assemblyName)
	{
		if (assemblyName.Name is not null && ContractAssemblies.Contains(assemblyName.Name))
			return AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
		string? path = _resolver.ResolveAssemblyToPath(assemblyName);
		return path is null ? null : LoadFromAssemblyPath(path);
	}

	public static void EnsureReferencesAllowed(string assemblyPath)
	{
		if (File.Exists(assemblyPath))
		{
			EnsureAssemblyReferencesAllowed(assemblyPath);
			return;
		}
		if (!Directory.Exists(assemblyPath)) throw new PluginException(PluginErrorCodes.PackageInvalid, $"程序集路径不存在: {assemblyPath}");
		foreach (string path in Directory.EnumerateFiles(assemblyPath, "*.dll", SearchOption.AllDirectories)) EnsureAssemblyReferencesAllowed(path);
	}

	private static void EnsureAssemblyReferencesAllowed(string assemblyPath)
	{
		try
		{
			using PEReader peReader = new(File.OpenRead(assemblyPath));
			MetadataReader metadata = peReader.GetMetadataReader();
			foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
			{
				string name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
				if (IsForbidden(name)) throw new PluginException(PluginErrorCodes.ForbiddenReference, $"插件引用了禁止的宿主程序集: {name}");
			}
		}
		catch (PluginException) { throw; }
		catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or IOException or InvalidOperationException)
		{
			throw new PluginException(PluginErrorCodes.PackageInvalid, "插件入口程序集无效", exception);
		}
	}

	private static bool IsForbidden(string name) => name.Equals("Nori.Core", StringComparison.OrdinalIgnoreCase)
		|| name.Equals("Nori.Desktop", StringComparison.OrdinalIgnoreCase)
		|| name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)
		|| name.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase);
}
