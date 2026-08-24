using Nori.Core.Live2D;
using Nori.Core.Resources;

namespace Nori.Core.Tests;

/// <summary>
/// model3.json 引用、固定模型 ID 与本地文件系统边界的安全校验.
/// </summary>
public sealed class Model3ReferenceValidatorTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"nori-model3-{Guid.NewGuid():N}");
	private readonly string _modelDir;

	public Model3ReferenceValidatorTests()
	{
		_modelDir = Path.Combine(_root, "model");
		Directory.CreateDirectory(_modelDir);
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(_root, true);
		}
		catch (IOException)
		{
		}
	}

	[Fact]
	public void 所有支持的模型引用都通过校验()
	{
		WriteFile("model.moc3", "MOC3");
		WriteFile("tex/0.png", "png");
		WriteFile("physics.physics3.json", "{}");
		WriteFile("pose.pose3.json", "{}");
		WriteFile("motions/idle.motion3.json", "{}");
		WriteFile("sounds/idle.wav", "wav");
		WriteFile("smile.exp3.json", "{\"Parameters\":[]}");
		WriteFile("model3.json", """
			{
			  "FileReferences": {
			    "Moc": "model.moc3",
			    "Textures": ["tex/0.png"],
			    "Physics": "physics.physics3.json",
			    "Pose": "pose.pose3.json",
			    "Motions": {"Idle": [{"File": "motions/idle.motion3.json", "Sound": "sounds/idle.wav"}]},
			    "Expressions": [{"Name": "Smile", "File": "smile.exp3.json"}]
			  }
			}
			""");

		Model3ReferenceValidator.Validate(_modelDir, Path.Combine(_modelDir, "model3.json"));
	}

	[Theory]
	[InlineData("../outside.moc3")]
	[InlineData("/tmp/outside.moc3")]
	[InlineData("\\\\server\\share\\outside.moc3")]
	[InlineData("C:\\outside.moc3")]
	public void 不安全的模型引用路径被拒绝(string reference)
	{
		string escapedReference = reference.Replace("\\", "\\\\").Replace("\"", "\\\"");
		WriteFile("model3.json", $"{{\"FileReferences\":{{\"Moc\":\"{escapedReference}\"}}}}");

		ResourceException error = Assert.Throws<ResourceException>(() =>
			Model3ReferenceValidator.Validate(_modelDir, Path.Combine(_modelDir, "model3.json")));
		Assert.Contains("model3.json 引用", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void 缺失的模型引用文件被拒绝()
	{
		WriteFile("model.moc3", "MOC3");
		WriteFile("model3.json", """
			{"FileReferences":{"Moc":"model.moc3","Textures":["missing.png"]}}
			""");

		ResourceException error = Assert.Throws<ResourceException>(() =>
			Model3ReferenceValidator.Validate(_modelDir, Path.Combine(_modelDir, "model3.json")));
		Assert.Contains("不存在", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("{}")]
	[InlineData("{\"FileReferences\":{\"Textures\":[]}}")]
	public void 缺少FileReferences或Moc时被拒绝(string json)
	{
		WriteFile("model3.json", json);
		ResourceException error = Assert.Throws<ResourceException>(() =>
			Model3ReferenceValidator.Validate(_modelDir, Path.Combine(_modelDir, "model3.json")));
		Assert.Contains("FileReferences", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void 无效Moc文件头被拒绝()
	{
		WriteFile("model.moc3", "NOT-MOC");
		WriteFile("model3.json", "{\"FileReferences\":{\"Moc\":\"model.moc3\",\"Textures\":[]}}");
		ResourceException error = Assert.Throws<ResourceException>(() =>
			Model3ReferenceValidator.Validate(_modelDir, Path.Combine(_modelDir, "model3.json")));
		Assert.Contains("Moc 文件头无效", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void 引用经过符号链接时被拒绝()
	{
		string outsideDir = Path.Combine(_root, "outside");
		Directory.CreateDirectory(outsideDir);
		File.WriteAllText(Path.Combine(outsideDir, "model.moc3"), "outside");
		try
		{
			File.CreateSymbolicLink(Path.Combine(_modelDir, "linked.moc3"), Path.Combine(outsideDir, "model.moc3"));
		}
		catch (Exception exception) when (exception is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
		{
			return;
		}
		WriteFile("model3.json", """
			{"FileReferences":{"Moc":"linked.moc3"}}
			""");

		ResourceException error = Assert.Throws<ResourceException>(() =>
			Model3ReferenceValidator.Validate(_modelDir, Path.Combine(_modelDir, "model3.json")));
		Assert.Contains("符号链接", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("ARGNori.model3.json", "arg-nori")]
	[InlineData("arg-nori.model3.json", "arg-nori")]
	[InlineData("Nori.model3.json", "nori")]
	[InlineData("arg-nori/model.model3.json", "arg-nori")]
	[InlineData("nori/model.model3.json", "nori")]
	public void 只解析两个固定模型ID(string path, string expected)
	{
		Assert.Equal(expected, SupportedModelIds.ResolveFromModelPath(path));
	}

	[Theory]
	[InlineData("Other.model3.json")]
	[InlineData("other/model.model3.json")]
	[InlineData("ARGNori_web/model.model3.json")]
	public void 任意模型名称不会被动态转换(string path)
	{
		Assert.Null(SupportedModelIds.ResolveFromModelPath(path));
	}

	private void WriteFile(string relativePath, string content)
	{
		string path = Path.Combine(_modelDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);
	}
}
