using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Nori.Plugin.Abstractions;
using Nori.Plugin.Arcade.Abstractions;
using Nori.Plugin.Games.Abstractions;
using Nori.Plugin.Harness.Abstractions;

namespace Nori.Plugin.Runtime;

/// <summary>每个插件独立的可回收依赖加载上下文。</summary>
public sealed class PluginLoadContext : AssemblyLoadContext
{
	private static readonly HashSet<string> ContractAssemblyNames =
	[
		typeof(INoriPlugin).Assembly.GetName().Name!,
		typeof(IGameProvider).Assembly.GetName().Name!,
		typeof(IArcadeCartridge).Assembly.GetName().Name!,
		typeof(IHarnessTool).Assembly.GetName().Name!,
	];

	private readonly AssemblyDependencyResolver _resolver;
	private readonly string _pluginRoot;

	public PluginLoadContext(string mainAssemblyPath)
		: base($"Nori.Plugin:{Path.GetFileNameWithoutExtension(mainAssemblyPath)}:{Guid.NewGuid():N}", isCollectible: true)
	{
		string fullPath = Path.GetFullPath(mainAssemblyPath);
		_resolver = new AssemblyDependencyResolver(fullPath);
		_pluginRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullPath) ?? fullPath, ".."));
	}

	protected override Assembly? Load(AssemblyName assemblyName)
	{
		if (assemblyName.Name is { } name && ContractAssemblyNames.Contains(name))
		{
			return name switch
			{
				_ when string.Equals(name, typeof(INoriPlugin).Assembly.GetName().Name, StringComparison.Ordinal) => typeof(INoriPlugin).Assembly,
				_ when string.Equals(name, typeof(IGameProvider).Assembly.GetName().Name, StringComparison.Ordinal) => typeof(IGameProvider).Assembly,
				_ when string.Equals(name, typeof(IArcadeCartridge).Assembly.GetName().Name, StringComparison.Ordinal) => typeof(IArcadeCartridge).Assembly,
				_ when string.Equals(name, typeof(IHarnessTool).Assembly.GetName().Name, StringComparison.Ordinal) => typeof(IHarnessTool).Assembly,
				_ => null,
			};
		}

		string? resolved = _resolver.ResolveAssemblyToPath(assemblyName);
		if (resolved is null || !IsWithin(_pluginRoot, resolved)) return null;
		return LoadFromAssemblyPath(resolved);
	}

	protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
	{
		string? resolved = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
		return resolved is not null && IsWithin(_pluginRoot, resolved)
			? LoadUnmanagedDllFromPath(resolved)
			: IntPtr.Zero;
	}

	/// <summary>预扫描插件目录，拒绝宿主内部引用与重复 contract DLL。</summary>
	public static void EnsureReferencesAllowed(string pluginDirectory)
	{
		if (!Directory.Exists(pluginDirectory)) throw new PluginException(PluginErrorCodes.InvalidPackage, "插件目录不存在");
		foreach (string path in Directory.EnumerateFiles(pluginDirectory, "*.dll", SearchOption.AllDirectories))
		{
			if (ContractAssemblyNames.Contains(Path.GetFileName(path)))
				throw new PluginException(PluginErrorCodes.ContractAssemblyDenied, "插件包不得携带宿主 contract 程序集");
			EnsureAssemblyReferencesAllowed(path);
		}
	}

	private static void EnsureAssemblyReferencesAllowed(string assemblyPath)
	{
		try
		{
			using FileStream stream = File.OpenRead(assemblyPath);
			using PEReader reader = new(stream);
			if (!reader.HasMetadata) throw new BadImageFormatException();
			MetadataReader metadata = reader.GetMetadataReader();
			foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
			{
				string name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
				if (IsForbidden(name)) throw new PluginException(PluginErrorCodes.ForbiddenReference, $"插件引用了禁止的宿主程序集: {name}");
			}
		}
		catch (PluginException)
		{
			throw;
		}
		catch (Exception exception) when (exception is BadImageFormatException or FileLoadException or IOException or UnauthorizedAccessException or InvalidOperationException)
		{
			throw new PluginException(PluginErrorCodes.InvalidPackage, "插件程序集格式无效", exception);
		}
	}

	private static bool IsForbidden(string name) =>
		name.Equals("Nori.Core", StringComparison.OrdinalIgnoreCase) ||
		name.Equals("Nori.Desktop", StringComparison.OrdinalIgnoreCase) ||
		name.Equals("Nori.Plugin.Runtime", StringComparison.OrdinalIgnoreCase) ||
		name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) ||
		name.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase);

	private static bool IsWithin(string root, string path)
	{
		string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
		string fullPath = Path.GetFullPath(path);
		StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison) || fullPath.Equals(fullRoot, comparison);
	}
}
