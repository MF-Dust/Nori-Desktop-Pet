using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Nori.Core.Configuration;
using Nori.Core.Data;
using Nori.Core.Voice;

namespace Nori.Core.Tests;

public class GeminiTtsProviderTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"nori-gemini-tts-{Guid.NewGuid():N}.db");
    private readonly NoriDatabase _database;
    private readonly ConfigStore _config;

    public GeminiTtsProviderTests()
    {
        _database = NoriDatabase.Open(_dbPath);
        _config = new ConfigStore(_database);
        _config.InitDefaults("0.1.0");
        _config.Set("tts_provider", new ConfigValue.Text("gemini"));
        _config.Set("tts_base_url", new ConfigValue.Text("https://relay.example/v1beta"));
        _config.Set("tts_api_key", new ConfigValue.Text("secret"));
        _config.Set("tts_model", new ConfigValue.Text("gemini-3.1-flash-tts-preview"));
    }

    [Fact]
    public async Task GenerateContentMapsRequestAndWrapsPcmAsWav()
    {
        CaptureHandler handler = new(_ => Success([1, 0, 2, 0]));
        using HttpClient client = new(handler);
        GeminiTtsProvider provider = new(client, _config);
        EncodedAudio audio = await provider.SynthesizeAsync("你好", new TtsSynthesizeOptions {Voice = "Kore"}, CancellationToken.None);

        Assert.Equal(new Uri("https://relay.example/v1beta/models/gemini-3.1-flash-tts-preview:generateContent"), handler.LastUri);
        Assert.Equal("secret", handler.ApiKey);
        Assert.Equal("audio/wav", audio.Mime);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(audio.Bytes, 0, 4));
        JsonNode body = JsonNode.Parse(handler.LastBody!)!;
        Assert.Equal("AUDIO", body["generationConfig"]?["responseModalities"]?[0]?.GetValue<string>());
        Assert.Equal("Kore", body["generationConfig"]?["speechConfig"]?["voiceConfig"]?["prebuiltVoiceConfig"]?["voiceName"]?.GetValue<string>());
    }

    [Fact]
    public void VoiceServiceCreatesGeminiProvider()
    {
        using HttpClient client = new(new CaptureHandler(_ => Success([0, 0])));
        using VoiceService service = new(client, _config, null, () => null);
        Assert.IsType<GeminiTtsProvider>(service.CreateProvider("gemini"));
    }

    public void Dispose()
    {
        _database.Dispose();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    private static HttpResponseMessage Success(byte[] pcm)
    {
        JsonObject payload = new()
        {
            ["candidates"] = new JsonArray
            {
                new JsonObject
                {
                    ["content"] = new JsonObject
                    {
                        ["parts"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["inlineData"] = new JsonObject
                                {
                                    ["mimeType"] = "audio/L16;codec=pcm;rate=24000",
                                    ["data"] = Convert.ToBase64String(pcm),
                                },
                            },
                        },
                    },
                },
            },
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        public string? ApiKey { get; private set; }
        public string? LastBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            ApiKey = request.Headers.TryGetValues("x-goog-api-key", out IEnumerable<string>? values) ? values.SingleOrDefault() : null;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }
}
