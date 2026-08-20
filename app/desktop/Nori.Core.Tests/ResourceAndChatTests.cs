using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Nori.Core.Chat;
using Nori.Core.Resources;

namespace Nori.Core.Tests;

/// <summary>
/// 动作标记解析, 对应 Rust 版 chat.rs 的 extract_motion_markers
/// </summary>
public class MotionMarkersTests
{
	[Fact]
	public void 剥离单个标记()
	{
		(string content, IReadOnlyList<string> motions) = MotionMarkers.Extract("你好呀。\n[nori_motion:smile]");
		Assert.Equal("你好呀。\n", content);
		Assert.Equal(["smile"], motions);
	}

	[Fact]
	public void 剥离多个标记并保留其余文本()
	{
		(string content, IReadOnlyList<string> motions) = MotionMarkers.Extract("A[nori_motion:a]B[nori_motion:b]C");
		Assert.Equal("ABC", content);
		Assert.Equal(["a", "b"], motions);
	}

	[Fact]
	public void 标记名去空白()
	{
		(_, IReadOnlyList<string> motions) = MotionMarkers.Extract("[nori_motion:  smile  ]");
		Assert.Equal(["smile"], motions);
	}

	[Fact]
	public void 空标记名被忽略()
	{
		(string content, IReadOnlyList<string> motions) = MotionMarkers.Extract("A[nori_motion:]B");
		Assert.Equal("AB", content);
		Assert.Empty(motions);
	}

	[Fact]
	public void 未闭合的标记原样保留()
	{
		(string content, IReadOnlyList<string> motions) = MotionMarkers.Extract("A[nori_motion:smile");
		Assert.Equal("A[nori_motion:smile", content);
		Assert.Empty(motions);
	}

	[Fact]
	public void 没有标记时原样返回()
	{
		(string content, IReadOnlyList<string> motions) = MotionMarkers.Extract("普通回复");
		Assert.Equal("普通回复", content);
		Assert.Empty(motions);
	}
}

/// <summary>
/// ZIP 解压安全规则, 对应 Rust 版 downloader.rs 的 sanitize_zip_path / extract_zip
/// </summary>
public class ZipExtractorTests
{
	[Theory]
	[InlineData("a/b/c.png", "a/b/c.png")]
	// 反斜杠归一化
	[InlineData("a\\b\\c.png", "a/b/c.png")]
	// 冗余的 . 与空段被折叠
	[InlineData("a/./b//c.png", "a/b/c.png")]
	public void 合法路径归一化(string raw, string expected) =>
		Assert.Equal(expected, ZipExtractor.SanitizePath(raw));

	[Theory]
	[InlineData("/etc/passwd", "绝对路径")]
	[InlineData("//server/share/x", "UNC")]
	[InlineData("C:/Windows/win.ini", "Windows 绝对路径")]
	[InlineData("a/../../etc/passwd", "路径穿越")]
	public void 非法路径被拒绝(string raw, string reason)
	{
		ResourceException error = Assert.Throws<ResourceException>(() => ZipExtractor.SanitizePath(raw));
		Assert.Contains(reason, error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void 控制字符被拒绝() =>
		Assert.Throws<ResourceException>(() => ZipExtractor.SanitizePath("a/\u0001b.png"));

	[Theory]
	// 所有条目共享同一个顶层目录 → 可以剥
	[InlineData(new[] {"arg-nori/a.json", "arg-nori/tex/b.png"}, "arg-nori")]
	// 顶层不唯一 → 不能剥
	[InlineData(new[] {"arg-nori/a.json", "other/b.png"}, null)]
	// 顶层有文件 → 不能剥
	[InlineData(new[] {"a.json", "arg-nori/b.png"}, null)]
	[InlineData(new[] {"a.json"}, null)]
	public void 识别唯一顶层目录(string[] paths, string? expected) =>
		Assert.Equal(expected, ZipExtractor.FindCommonTopDirectory(paths));

	[Fact]
	public void 解压时剥掉多余顶层目录()
	{
		string root = Path.Combine(Path.GetTempPath(), $"nori-zip-{Guid.NewGuid():N}");
		string zipPath = Path.Combine(root, "pack.zip");
		string target = Path.Combine(root, "out");
		Directory.CreateDirectory(root);
		try
		{
			// 模拟"多包了一层同名目录"的资源包
			using (FileStream stream = File.Create(zipPath))
			using (ZipArchive archive = new(stream, ZipArchiveMode.Create))
			{
				using (StreamWriter writer = new(archive.CreateEntry("arg-nori/ARGNori.model3.json").Open()))
				{
					writer.Write("""{"Version":3}""");
				}
				using (StreamWriter writer = new(archive.CreateEntry("arg-nori/tex/t.png").Open()))
				{
					writer.Write("x");
				}
			}

			ZipExtractor.Extract(zipPath, target);

			// 顶层目录被剥掉, 文件直接落在目标目录下 —— 前端按 live2d/<id>/<file> 就能取到
			Assert.True(File.Exists(Path.Combine(target, "ARGNori.model3.json")));
			Assert.True(File.Exists(Path.Combine(target, "tex", "t.png")));
			Assert.False(Directory.Exists(Path.Combine(target, "arg-nori")));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Fact]
	public void 顶层不唯一时保持原结构()
	{
		string root = Path.Combine(Path.GetTempPath(), $"nori-zip-{Guid.NewGuid():N}");
		string zipPath = Path.Combine(root, "pack.zip");
		string target = Path.Combine(root, "out");
		Directory.CreateDirectory(root);
		try
		{
			using (FileStream stream = File.Create(zipPath))
			using (ZipArchive archive = new(stream, ZipArchiveMode.Create))
			{
				using (StreamWriter writer = new(archive.CreateEntry("ARGNori.model3.json").Open()))
				{
					writer.Write("{}");
				}
				using (StreamWriter writer = new(archive.CreateEntry("tex/t.png").Open()))
				{
					writer.Write("x");
				}
			}

			ZipExtractor.Extract(zipPath, target);
			Assert.True(File.Exists(Path.Combine(target, "ARGNori.model3.json")));
			Assert.True(File.Exists(Path.Combine(target, "tex", "t.png")));
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}
}

/// <summary>
/// 资源名称校验, 对应 Rust 版 validate_resource_name
/// </summary>
public class ResourceNameTests
{
	[Theory]
	[InlineData("arg-nori", true)]
	[InlineData("nori", true)]
	[InlineData("", false)]
	[InlineData(".", false)]
	[InlineData("..", false)]
	[InlineData("a/b", false)]
	[InlineData("a\\b", false)]
	[InlineData("C:", false)]
	public void 名称校验(string name, bool expected) =>
		Assert.Equal(expected, ResourceName.IsValid(name));

	[Fact]
	public void 控制字符被拒绝() => Assert.False(ResourceName.IsValid("a\u0001b"));
}

public class ResourceImportTests
{
	[Fact]
	public void Import_从本地ZIP导入模型()
	{
		string tempDir = Path.Combine(Path.GetTempPath(), $"nori-import-{Guid.NewGuid():N}");
		string zipPath = Path.Combine(tempDir, "pack.zip");
		Directory.CreateDirectory(tempDir);
		try
		{
			using (FileStream stream = File.Create(zipPath))
			using (ZipArchive archive = new(stream, ZipArchiveMode.Create))
			{
				using (StreamWriter writer = new(archive.CreateEntry("ARGNori_web/ARGNori.model3.json").Open()))
				{
					writer.Write("""{"Version":3}""");
				}
				using (StreamWriter writer = new(archive.CreateEntry("ARGNori_web/tex/0.png").Open()))
				{
					writer.Write("img");
				}
			}

			using HttpClient client = new();
			ResourceManager manager = new(client, tempDir);
			IReadOnlyList<string> imported = manager.Import(ResourceType.Live2D, zipPath);

			Assert.Contains("arg-nori", imported);
			Assert.True(manager.IsInstalled(ResourceType.Live2D, "arg-nori"));
			Assert.True(File.Exists(Path.Combine(tempDir, "resources", "live2d", "arg-nori", "ARGNori.model3.json")));
			Assert.True(File.Exists(Path.Combine(tempDir, "resources", "live2d", "arg-nori", "tex", "0.png")));
		}
		finally
		{
			Directory.Delete(tempDir, true);
		}
	}
}

public class ResourceDownloadTests
{
	[Fact]
	public async Task Ensure_支持断点续传并校验Manifest哈希()
	{
		string tempDir = Path.Combine(Path.GetTempPath(), $"nori-download-{Guid.NewGuid():N}");
		Directory.CreateDirectory(Path.Combine(tempDir, "temp"));
		byte[] zip = CreateModelZip();
		string hash = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
		int prefixLength = 5;
		File.WriteAllBytes(Path.Combine(tempDir, "temp", "arg-nori.zip.part"), zip[..prefixLength]);
		long? rangeStart = null;

		using MockHttpMessageHandler handler = new(request =>
		{
			string path = request.RequestUri?.AbsolutePath ?? "";
			if (path.EndsWith("/resource/manifest", StringComparison.Ordinal))
			{
				return JsonResponse($"{{\"body\":{{\"type\":\"live2d\",\"name\":\"arg-nori\",\"version\":\"1.0.0\",\"size\":{zip.Length},\"sha256\":\"{hash}\",\"object\":\"live2d/arg-nori.zip\"}},\"error\":false,\"message\":\"\",\"timestamp\":0}}");
			}
			if (path.EndsWith("/resource/download_url", StringComparison.Ordinal))
			{
				return JsonResponse("{\"body\":{\"url\":\"https://cdn.test/arg-nori.zip\"},\"error\":false,\"message\":\"\",\"timestamp\":0}");
			}

			long start = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
			rangeStart = start;
			byte[] body = zip[(int)start..];
			return new HttpResponseMessage(start > 0 ? System.Net.HttpStatusCode.PartialContent : System.Net.HttpStatusCode.OK)
			{
				Content = new ByteArrayContent(body),
			};
		});

		try
		{
			using HttpClient client = new(handler);
			ResourceManager manager = new(client, tempDir, "https://api.test/nori");
			List<string> steps = [];
			await manager.EnsureAsync(ResourceType.Live2D, "arg-nori", step => steps.Add(step.Step));

			Assert.Equal(prefixLength, rangeStart);
			Assert.Contains("done", steps);
			Assert.True(manager.IsInstalled(ResourceType.Live2D, "arg-nori"));
			Assert.True(File.Exists(Path.Combine(tempDir, "resources", "live2d", "arg-nori", "ARGNori.model3.json")));
		}
		finally
		{
			Directory.Delete(tempDir, true);
		}
	}

	private static byte[] CreateModelZip()
	{
		using MemoryStream stream = new();
		using (ZipArchive archive = new(stream, ZipArchiveMode.Create, true))
		using (StreamWriter writer = new(archive.CreateEntry("ARGNori.model3.json").Open(), Encoding.UTF8))
		{
			writer.Write("{\"Version\":3}");
		}
		return stream.ToArray();
	}

	private static HttpResponseMessage JsonResponse(string json) => new()
	{
		StatusCode = System.Net.HttpStatusCode.OK,
		Content = new StringContent(json, Encoding.UTF8, "application/json"),
	};

	private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handler(request));
	}
}

public class ChatServiceTests : IDisposable
{
	private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Task.FromResult(handler(request));
		}
	}

	private readonly string _path = Path.Combine(Path.GetTempPath(), $"nori-chat-test-{Guid.NewGuid():N}.db");
	private readonly Nori.Core.Data.NoriDatabase _database;
	private readonly Nori.Core.Configuration.ConfigStore _config;

	public ChatServiceTests()
	{
		_database = Nori.Core.Data.NoriDatabase.Open(_path);
		_config = new Nori.Core.Configuration.ConfigStore(_database);
		_config.InitDefaults("0.1.0");
	}

	public void Dispose()
	{
		_database.Dispose();
		try
		{
			File.Delete(_path);
		}
		catch (IOException)
		{
		}
		GC.SuppressFinalize(this);
	}

	[Fact]
	public async Task ChatService_StreamAsync流式回调与动作提取()
	{
		using MockHttpMessageHandler handler = new(req =>
		{
			string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"主人好呀！\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"[nori_motion:smile]\"}}]}\n\ndata: [DONE]\n\n";
			return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
			{
				Content = new StringContent(sse, System.Text.Encoding.UTF8, "text/event-stream")
			};
		});

		using HttpClient client = new(handler);
		ChatService chat = new(client, _database, _config);

		List<string> chunks = [];
		List<string> motions = [];

		string final = await chat.StreamAsync(
			"openai",
			"https://api.openai.com/v1",
			"key",
			"gpt-4o",
			[new ChatMessageInput {Role = "user", Content = "hello"}],
			chunk => chunks.Add(chunk),
			motion => motions.Add(motion));

		Assert.Equal("主人好呀！", final);
		Assert.Equal(["smile"], motions);
		Assert.Equal(2, chat.GetHistory().Count);
	}

	[Fact]
	public void ChatService_SaveAndClearHistory()
	{
		using HttpClient client = new();
		ChatService chat = new(client, _database, _config);

		chat.SaveMessage("user", "你好呀");
		chat.SaveMessage("assistant", "主人好！");

		IReadOnlyList<ChatMessage> history = chat.GetHistory();
		Assert.Equal(2, history.Count);
		Assert.Equal("user", history[0].Role);
		Assert.Equal("你好呀", history[0].Content);
		Assert.Equal("assistant", history[1].Role);
		Assert.Equal("主人好！", history[1].Content);

		chat.ClearHistory();
		Assert.Empty(chat.GetHistory());
	}
}
