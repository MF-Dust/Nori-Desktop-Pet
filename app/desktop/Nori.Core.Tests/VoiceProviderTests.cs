using System.Net;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Voice;

namespace Nori.Core.Tests;

/// <summary>HTTP 语音提供商的格式与降级测试。</summary>
public class VoiceProviderTests : IDisposable
{
	private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nori-voice-{Guid.NewGuid():N}.db");
	private readonly NoriDatabase _database;
	private readonly ConfigStore _config;

	public VoiceProviderTests()
	{
		_database = NoriDatabase.Open(_dbPath);
		_config = new ConfigStore(_database);
		_config.InitDefaults("0.1.0");
		_config.Set("gptsovits_base_url", new ConfigValue.Text("http://127.0.0.1:9880"));
	}

	[Fact]
	public async Task GPTSoVITS_POST非成功时降级GET()
	{
		RecordingHandler handler = new(request => request.Method == HttpMethod.Post
			? new HttpResponseMessage(HttpStatusCode.InternalServerError)
			: new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = AudioContent([9, 8, 7], "audio/wav"),
			});
		using HttpClient client = new(handler);
		GptSoVitsTtsProvider provider = new(client, _config);

		EncodedAudio audio = await provider.SynthesizeAsync("你好", new TtsSynthesizeOptions(), CancellationToken.None);

		Assert.Equal(new byte[] {9, 8, 7}, audio.Bytes);
		Assert.Equal("audio/wav", audio.Mime);
		Assert.Equal([HttpMethod.Post, HttpMethod.Get], handler.Methods);
	}

	[Fact]
	public async Task GPTSoVITS_POST空响应时降级GET()
	{
		RecordingHandler handler = new(request =>
		{
			if (request.Method == HttpMethod.Post)
			{
				HttpResponseMessage response = new(HttpStatusCode.OK) {Content = new ByteArrayContent([])};
				response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
				return response;
			}
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = AudioContent([1, 2], "audio/wav"),
			};
		});
		using HttpClient client = new(handler);
		GptSoVitsTtsProvider provider = new(client, _config);

		EncodedAudio audio = await provider.SynthesizeAsync("你好", new TtsSynthesizeOptions(), CancellationToken.None);

		Assert.Equal(new byte[] {1, 2}, audio.Bytes);
		Assert.Equal([HttpMethod.Post, HttpMethod.Get], handler.Methods);
	}

	[Fact]
	public async Task TTS缺少或错误MIME时失败()
	{
		RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new ByteArrayContent([1]),
		});
		using HttpClient client = new(handler);
		GptSoVitsTtsProvider provider = new(client, _config);

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			provider.SynthesizeAsync("你好", new TtsSynthesizeOptions(), CancellationToken.None));
	}

	private static ByteArrayContent AudioContent(byte[] bytes, string mime)
	{
		ByteArrayContent content = new(bytes);
		content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
		return content;
	}

	public void Dispose()
	{
		_database.Dispose();
		try { File.Delete(_dbPath); } catch (IOException) { }
	}

	private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
	{
		public List<HttpMethod> Methods { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Methods.Add(request.Method);
			HttpResponseMessage response = responder(request);
			if (request.Method == HttpMethod.Get && response.Content is null)
			{
				response.Content = AudioContent([9, 8, 7], "audio/wav");
			}
			return Task.FromResult(response);
		}
	}
}
