using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Voice;

namespace Nori.Core.Tests;

/// <summary>PR #33 审查问题的回归测试。</summary>
public sealed class IndexTtsReviewRegressionTests : IDisposable
{
	private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nori-indextts-review-{Guid.NewGuid():N}.db");
	private readonly NoriDatabase _database;
	private readonly ConfigStore _config;

	public IndexTtsReviewRegressionTests()
	{
		_database = NoriDatabase.Open(_dbPath);
		_config = new ConfigStore(_database);
		_config.InitDefaults("0.1.0");
		_config.Set("tts_provider", new ConfigValue.Text("indextts"));
		_config.Set("tts_api_key", new ConfigValue.Text("test-secret"));
	}

	[Fact]
	public async Task 完整Speech端点克隆时会归一化到VoiceUpload且使用配置模型()
	{
		string tempDir = CreateTempDir();
		string template = Path.Combine(tempDir, "voice.wav");
		File.WriteAllBytes(template, MinimalWav());
		_config.Set("tts_base_url", new ConfigValue.Text("https://api.modelverse.cn/v1/audio/speech"));
		_config.Set("tts_model", new ConfigValue.Text("custom/index-tts-model"));

		RecordingHandler handler = new(request =>
			Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"id":"uspeech:clone"}""")));
		using HttpClient client = new(handler);
		IndexTtsProvider provider = new(client, _config, new AppStoragePaths(tempDir));

		await provider.CloneVoiceAsync(template, CancellationToken.None);

		Assert.Equal(new Uri("https://api.modelverse.cn/v1/audio/voice/upload"), handler.LastUri);
		Assert.Contains("custom/index-tts-model", handler.LastBody, StringComparison.Ordinal);
	}

	[Fact]
	public async Task 原模板删除后过期音色仍从本地存档续期()
	{
		string tempDir = CreateTempDir();
		string template = Path.Combine(tempDir, "voice.wav");
		File.WriteAllBytes(template, MinimalWav());
		AppStoragePaths paths = new(tempDir);
		_config.Set("indextts_template_audio", new ConfigValue.Text(template));

		int uploadCount = 0;
		RecordingHandler handler = new(request =>
		{
			if (request.RequestUri?.AbsolutePath.EndsWith("/audio/voice/upload", StringComparison.Ordinal) == true)
			{
				uploadCount++;
				return Task.FromResult(JsonResponse(HttpStatusCode.OK, $$"""{"id":"uspeech:renew-{{uploadCount}}"}"""));
			}
			return Task.FromResult(WavResponse());
		});
		using HttpClient client = new(handler);
		IndexTtsProvider provider = new(client, _config, paths);

		Assert.Equal("uspeech:renew-1", await provider.CloneVoiceAsync(template, CancellationToken.None));

		IndexTtsProvider.IndexTtsVoiceCache cache = JsonSerializer.Deserialize<IndexTtsProvider.IndexTtsVoiceCache>(
			File.ReadAllText(paths.IndexTtsCachePath))!;
		IndexTtsProvider.IndexTtsVoiceEntry entry = Assert.Single(cache.Voices.Values);
		string archivedFile = entry.ArchiveFile;
		entry.UploadUnixSeconds = DateTimeOffset.UtcNow.AddDays(-8).ToUnixTimeSeconds();
		File.WriteAllText(paths.IndexTtsCachePath, JsonSerializer.Serialize(cache));
		File.Delete(template);

		string renewed = await provider.ResolveTemplateVoiceAsync(CancellationToken.None);

		Assert.Equal("uspeech:renew-2", renewed);
		Assert.Equal(2, uploadCount);
		Assert.True(File.Exists(archivedFile));
	}

	[Fact]
	public async Task 情绪强度零值按零发送()
	{
		_config.Set("indextts_emo_alpha", new ConfigValue.Text("0"));
		RecordingHandler handler = new(_ => Task.FromResult(WavResponse()));
		using HttpClient client = new(handler);
		IndexTtsProvider provider = new(client, _config);

		await provider.SynthesizeAsync(
			"测试",
			new TtsSynthesizeOptions {Voice = "uspeech:test", EmotionText = "happy"},
			CancellationToken.None);

		JsonNode body = JsonNode.Parse(handler.LastBody)!;
		Assert.Equal(0d, body["emo_weight"]?.GetValue<double>());
	}

	[Fact]
	public async Task 情绪强度变化会生成新的合成缓存身份()
	{
		int synthCount = 0;
		RecordingHandler handler = new(request =>
		{
			if (request.RequestUri?.AbsolutePath.EndsWith("/audio/speech", StringComparison.Ordinal) == true) synthCount++;
			return Task.FromResult(WavResponse());
		});
		using HttpClient client = new(handler);
		using VoiceService service = new(client, _config, null, () => null);
		TtsSynthesizeOptions options = new() {Voice = "uspeech:test", EmotionText = "happy"};

		_config.Set("indextts_emo_alpha", new ConfigValue.Text("0.2"));
		await service.SynthesizeAsync("同一句话", options, CancellationToken.None);
		_config.Set("indextts_emo_alpha", new ConfigValue.Text("0.8"));
		await service.SynthesizeAsync("同一句话", options, CancellationToken.None);

		Assert.Equal(2, synthCount);
	}

	[Theory]
	[InlineData("𠮷野家", "𠮷野家")]
	[InlineData("数学字符𝕏仍保留", "数学字符𝕏仍保留")]
	[InlineData("𠮷野家😊", "𠮷野家")]
	public void 非Emoji补充平面字符会保留(string input, string expected)
	{
		Assert.Equal(expected, VoiceTextSanitizer.StripKaomoji(input));
	}

	public void Dispose()
	{
		_database.Dispose();
		try { File.Delete(_dbPath); } catch (IOException) { }
	}

	private static string CreateTempDir()
	{
		string path = Path.Combine(Path.GetTempPath(), $"nori-indextts-review-files-{Guid.NewGuid():N}");
		Directory.CreateDirectory(path);
		return path;
	}

	private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
	{
		Content = new StringContent(json, Encoding.UTF8, "application/json"),
	};

	private static HttpResponseMessage WavResponse() => new(HttpStatusCode.OK)
	{
		Content = new ByteArrayContent(MinimalWav())
		{
			Headers = {ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav")},
		},
	};

	private static byte[] MinimalWav()
	{
		using MemoryStream stream = new(44);
		using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
		{
			writer.Write(Encoding.ASCII.GetBytes("RIFF"));
			writer.Write(36);
			writer.Write(Encoding.ASCII.GetBytes("WAVE"));
			writer.Write(Encoding.ASCII.GetBytes("fmt "));
			writer.Write(16);
			writer.Write((short)1);
			writer.Write((short)1);
			writer.Write(24000);
			writer.Write(48000);
			writer.Write((short)2);
			writer.Write((short)16);
			writer.Write(Encoding.ASCII.GetBytes("data"));
			writer.Write(0);
		}
		return stream.ToArray();
	}

	private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
	{
		public Uri? LastUri { get; private set; }
		public string LastBody { get; private set; } = "";

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			LastUri = request.RequestUri;
			LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
			return await responder(request);
		}
	}
}
