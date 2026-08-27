using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nori.Core.Configuration;
using Nori.Core.Network;

namespace Nori.Core.Voice;

/// <summary>Gemini 原生 TTS 适配器 (models/{model}:generateContent)。</summary>
public sealed class GeminiTtsProvider(HttpClient httpClient, ConfigStore config) : ITtsProvider
{
    private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";
    private const string DefaultModel = "gemini-3.1-flash-tts-preview";
    private const int DefaultSampleRate = 24000;

    public string Name => "gemini";

    public async Task<EncodedAudio> SynthesizeAsync(string text, TtsSynthesizeOptions options, CancellationToken cancellationToken)
    {
        string baseUrl = (config.GetStringOr("tts_base_url", "") is {Length: > 0} saved ? saved : DefaultBaseUrl).Trim().TrimEnd('/');
        string apiKey = config.GetStringOr("tts_api_key", "").Trim();
        string model = config.GetStringOr("tts_model", DefaultModel).Trim();
        string voice = options.Voice is {Length: > 0} requested ? requested.Trim() : "Kore";
        if (model.Length == 0 || model.Equals("tts-1", StringComparison.OrdinalIgnoreCase)) model = DefaultModel;
        if (voice.Length == 0) voice = "Kore";

        string endpoint = BuildEndpoint(baseUrl, model);
        UrlAccessPolicy.EnsureAllowed(new Uri(endpoint), allowPrivate: true);

        JsonObject payload = new()
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["parts"] = new JsonArray {new JsonObject {["text"] = text}},
                },
            },
            ["generationConfig"] = new JsonObject
            {
                ["responseModalities"] = new JsonArray {"AUDIO"},
                ["speechConfig"] = new JsonObject
                {
                    ["voiceConfig"] = new JsonObject
                    {
                        ["prebuiltVoiceConfig"] = new JsonObject {["voiceName"] = voice},
                    },
                },
            },
        };

        using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (apiKey.Length > 0) request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        byte[] responseBytes = await VoiceHttpContent.ReadBytesAsync(response.Content, cancellationToken, allowEmpty: true);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Gemini TTS 请求失败: HTTP {(int)response.StatusCode} {ReadError(responseBytes)}".TrimEnd());
        }
        if (responseBytes.Length == 0) throw new InvalidOperationException("Gemini TTS 响应为空");

        using JsonDocument document = JsonDocument.Parse(responseBytes);
        if (!TryReadInlineAudio(document.RootElement, out string? encoded, out string? mime) || string.IsNullOrWhiteSpace(encoded))
            throw new InvalidOperationException("Gemini TTS 响应未包含 inlineData 音频数据");

        byte[] audioBytes;
        try { audioBytes = Convert.FromBase64String(encoded); }
        catch (FormatException exception) { throw new InvalidOperationException("Gemini TTS 返回的音频 Base64 无效", exception); }

        if (IsPcmMime(mime))
        {
            int sampleRate = ReadSampleRate(mime) ?? DefaultSampleRate;
            return AudioMime.ValidateEncoded(WrapPcm16LeAsWav(audioBytes, sampleRate), "audio/wav");
        }
        if (!AudioMime.IsSupported(mime)) throw new InvalidOperationException($"Gemini TTS 返回了不支持的音频 MIME: {mime ?? "<empty>"}");
        return AudioMime.ValidateEncoded(audioBytes, mime);
    }

    internal static string BuildEndpoint(string baseUrl, string model)
    {
        string normalized = baseUrl.Trim().TrimEnd('/');
        if (normalized.EndsWith(":generateContent", StringComparison.OrdinalIgnoreCase)) return normalized;
        string escapedModel = Uri.EscapeDataString(model.Trim());
        return normalized.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
            ? $"{normalized}/{escapedModel}:generateContent"
            : $"{normalized}/models/{escapedModel}:generateContent";
    }

    private static bool TryReadInlineAudio(JsonElement root, out string? data, out string? mime)
    {
        data = null;
        mime = null;
        if (!root.TryGetProperty("candidates", out JsonElement candidates) || candidates.ValueKind != JsonValueKind.Array) return false;
        foreach (JsonElement candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out JsonElement content)
                || !content.TryGetProperty("parts", out JsonElement parts)
                || parts.ValueKind != JsonValueKind.Array) continue;
            foreach (JsonElement part in parts.EnumerateArray())
            {
                if (!part.TryGetProperty("inlineData", out JsonElement inlineData) || inlineData.ValueKind != JsonValueKind.Object) continue;
                if (!inlineData.TryGetProperty("data", out JsonElement dataElement) || dataElement.ValueKind != JsonValueKind.String) continue;
                data = dataElement.GetString();
                mime = inlineData.TryGetProperty("mimeType", out JsonElement mimeElement) && mimeElement.ValueKind == JsonValueKind.String
                    ? mimeElement.GetString()
                    : "audio/L16;codec=pcm;rate=24000";
                return true;
            }
        }
        return false;
    }

    private static string ReadError(byte[] bytes)
    {
        if (bytes.Length == 0) return "";
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            if (document.RootElement.TryGetProperty("error", out JsonElement error)
                && error.TryGetProperty("message", out JsonElement message)
                && message.ValueKind == JsonValueKind.String) return message.GetString() ?? "";
        }
        catch (JsonException) { }
        return Encoding.UTF8.GetString(bytes);
    }

    private static bool IsPcmMime(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime)) return true;
        string mediaType = mime.Split(';', 2)[0].Trim();
        return mediaType.Equals("audio/L16", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("audio/pcm", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("audio/raw", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ReadSampleRate(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime)) return null;
        foreach (string part in mime.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (part.StartsWith("rate=", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(part[5..].Trim('"'), out int rate) && rate is >= 8000 and <= 192000) return rate;
        return null;
    }

    private static byte[] WrapPcm16LeAsWav(byte[] pcm, int sampleRate)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        if (pcm.Length == 0) throw new InvalidOperationException("Gemini TTS 返回的音频为空");
        if (pcm.Length > VoiceAudioLimits.MaxBytes - 44) throw new InvalidOperationException("Gemini TTS 音频超过大小限制");
        using MemoryStream stream = new(44 + pcm.Length);
        using (BinaryWriter writer = new(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcm.Length);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * bitsPerSample / 8);
            writer.Write((short)(channels * bitsPerSample / 8));
            writer.Write(bitsPerSample);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(pcm.Length);
            writer.Write(pcm);
        }
        return stream.ToArray();
    }
}
