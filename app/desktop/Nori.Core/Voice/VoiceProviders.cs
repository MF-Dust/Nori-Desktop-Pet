using System.Text;
using System.Text.Json.Nodes;
using Nori.Core.Configuration;

namespace Nori.Core.Voice;

/// <summary>
/// OpenAI 兼容 TTS 适配器 (/v1/audio/speech, 返回 mp3)
/// </summary>
public sealed class OpenAiTtsProvider(HttpClient httpClient, ConfigStore config) : ITtsProvider
{
	public string Name => "openai";

	public async Task<byte[]> SynthesizeAsync(string text, TtsSynthesizeOptions options, CancellationToken cancellationToken)
	{
		string baseUrl = (config.GetStringOr("tts_base_url", "") is {Length: > 0} saved ? saved : "https://api.openai.com/v1").Trim().TrimEnd('/');
		string apiKey = config.GetStringOr("tts_api_key", "");
		if (!baseUrl.EndsWith("/audio/speech", StringComparison.OrdinalIgnoreCase))
		{
			baseUrl += "/audio/speech";
		}

		JsonObject payload = new()
		{
			["model"] = "tts-1",
			["input"] = text,
			["voice"] = options.Voice is {Length: > 0} ? options.Voice : "nova",
			["speed"] = options.Speed,
		};

		using HttpRequestMessage request = new(HttpMethod.Post, baseUrl)
		{
			Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
		};
		request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

		using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			string error = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new HttpRequestException($"OpenAI TTS 请求失败: HTTP {(int)response.StatusCode} {error}");
		}
		return await response.Content.ReadAsByteArrayAsync(cancellationToken);
	}
}

/// <summary>
/// 自定义 HTTP TTS 适配器
///
/// POST tts_base_url, 请求体 {text, voice, speed, pitch}, 响应为音频字节。
/// </summary>
public sealed class CustomHttpTtsProvider(HttpClient httpClient, ConfigStore config) : ITtsProvider
{
	public string Name => "custom";

	public async Task<byte[]> SynthesizeAsync(string text, TtsSynthesizeOptions options, CancellationToken cancellationToken)
	{
		string endpoint = config.GetStringOr("tts_base_url", "").Trim();
		if (endpoint.Length == 0) throw new InvalidOperationException("未配置自定义 TTS 请求端点 URL");

		Nori.Core.Network.UrlAccessPolicy.EnsureAllowed(new Uri(endpoint), allowPrivate: true);

		JsonObject payload = new()
		{
			["text"] = text,
			["voice"] = options.Voice,
			["speed"] = options.Speed,
		};

		using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
		{
			Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
		};
		using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"自定义 TTS 请求失败: HTTP {(int)response.StatusCode}");
		}
		return await response.Content.ReadAsByteArrayAsync(cancellationToken);
	}
}

/// <summary>
/// GPT-SoVITS API 适配器 (官方 FastAPI /tts 端点; 本地端点显式允许私网)
/// </summary>
public sealed class GptSoVitsTtsProvider(HttpClient httpClient, ConfigStore config) : ITtsProvider
{
	public string Name => "gpt_sovits";

	public async Task<byte[]> SynthesizeAsync(string text, TtsSynthesizeOptions options, CancellationToken cancellationToken)
	{
		string baseUrl = (config.GetStringOr("gptsovits_base_url", "") is {Length: > 0} saved ? saved : "http://127.0.0.1:9880").Trim().TrimEnd('/');
		string refAudio = config.GetStringOr("gptsovits_ref_audio", "");
		string promptText = config.GetStringOr("gptsovits_prompt_text", "");
		string promptLang = config.GetStringOr("gptsovits_prompt_lang", "zh");
		string url = baseUrl.EndsWith("/tts", StringComparison.OrdinalIgnoreCase) ? baseUrl : $"{baseUrl}/tts";

		Nori.Core.Network.UrlAccessPolicy.EnsureAllowed(new Uri(baseUrl), allowPrivate: true);

		JsonObject payload = new()
		{
			["text"] = text,
			["text_lang"] = "zh",
			["ref_audio_path"] = refAudio,
			["prompt_text"] = promptText,
			["prompt_lang"] = promptLang,
			["speed_factor"] = options.Speed,
		};

		// 优先尝试 POST JSON，降级 GET Query (与旧前端行为一致)
		byte[]? result = null;
		try
		{
			result = await PostAsync(url, payload, cancellationToken);
		}
		catch (HttpRequestException)
		{
			// 降级路径
		}

		result ??= await GetAsync(url, text, refAudio, promptText, promptLang, options.Speed, cancellationToken);
		return result;
	}

	private async Task<byte[]> PostAsync(string url, JsonObject payload, CancellationToken ct)
	{
		using HttpRequestMessage request = new(HttpMethod.Post, url)
		{
			Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
		};
		using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
		if (!response.IsSuccessStatusCode) return [];
		return await response.Content.ReadAsByteArrayAsync(ct);
	}

	private async Task<byte[]> GetAsync(
		string url, string text, string refAudio, string promptText, string promptLang, double speed, CancellationToken ct)
	{
		List<string> query =
		[
			$"text={Uri.EscapeDataString(text)}",
			"text_lang=zh",
			$"ref_audio_path={Uri.EscapeDataString(refAudio)}",
			$"prompt_text={Uri.EscapeDataString(promptText)}",
			$"prompt_lang={Uri.EscapeDataString(promptLang)}",
			$"speed_factor={speed.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
		];
		using HttpResponseMessage response = await httpClient.GetAsync($"{url}?{string.Join("&", query)}", ct);
		if (!response.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"GPT-SoVITS API 合成失败: HTTP {(int)response.StatusCode}");
		}
		return await response.Content.ReadAsByteArrayAsync(ct);
	}
}

/// <summary>
/// OpenAI Whisper 录音识别 (/v1/audio/transcriptions, multipart 上传 WAV)
/// </summary>
public sealed class WhisperSttProvider(HttpClient httpClient, ConfigStore config)
{
	/// <summary>识别一段录音并返回文本</summary>
	public async Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken)
	{
		string baseUrl = (config.GetStringOr("stt_base_url", "") is {Length: > 0} saved ? saved : "https://api.openai.com/v1").Trim();
		baseUrl = baseUrl.TrimEnd('/');
		if (!baseUrl.EndsWith("/audio/transcriptions", StringComparison.OrdinalIgnoreCase))
		{
			baseUrl += "/audio/transcriptions";
		}
		string apiKey = config.GetStringOr("stt_api_key", "");

		using MultipartFormDataContent form = new();
		form.Add(new ByteArrayContent(wavBytes), "file", "speech.wav");
		form.Add(new StringContent("whisper-1"), "model");
		form.Add(new StringContent("zh"), "language");

		using HttpRequestMessage request = new(HttpMethod.Post, baseUrl) {Content = form};
		request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

		using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			string error = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new HttpRequestException($"Whisper 识别失败: HTTP {(int)response.StatusCode} {error}");
		}
		JsonNode? data = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
		return data?["text"]?.GetValue<string>() ?? "";
	}
}
