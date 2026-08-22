using System.ClientModel;
using System.ClientModel.Primitives;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Embeddings;
using AiEmbeddingGenerationOptions = Microsoft.Extensions.AI.EmbeddingGenerationOptions;

namespace Nori.Core.Embedding;

/// <summary>
/// OpenAI 规范 Embedding 适配器。
/// 使用官方 EmbeddingClient + IEmbeddingGenerator，并在进程内用 8 MiB 分布式缓存包装。
/// </summary>
public sealed class OpenAiEmbeddingAdapter(HttpClient httpClient) : IEmbeddingAdapter
{
	private const long CacheSizeLimit = 8 * 1024 * 1024;
	private readonly HttpClient _httpClient = httpClient;
	private readonly Lock _gate = new();
	private GeneratorState? _state;
	private long _cacheEpoch;

	public async Task<float[]> GetEmbeddingAsync(
		string baseUrl,
		string apiKey,
		string model,
		string input,
		int? dimensions = null,
		CancellationToken cancellationToken = default)
	{
		IReadOnlyList<float[]> results = await GetEmbeddingsAsync(baseUrl, apiKey, model, [input], dimensions, cancellationToken);
		if (results.Count == 0) throw new InvalidOperationException("Embedding 服务未返回任何向量数据。");
		return results[0];
	}

	public async Task<IReadOnlyList<float[]>> GetEmbeddingsAsync(
		string baseUrl,
		string apiKey,
		string model,
		IReadOnlyList<string> inputs,
		int? dimensions = null,
		CancellationToken cancellationToken = default)
	{
		if (inputs.Count == 0) return [];
		string normalizedBase = NormalizeBaseUrl(baseUrl);
		string normalizedModel = string.IsNullOrWhiteSpace(model) ? "BAAI/bge-m3" : model.Trim();
		string fingerprint = Fingerprint(normalizedBase, apiKey, normalizedModel, dimensions);
		IEmbeddingGenerator<string, Embedding<float>> generator = GetGenerator(
			normalizedBase, apiKey, normalizedModel, dimensions, fingerprint);

		AiEmbeddingGenerationOptions options = new()
		{
			ModelId = normalizedModel,
			Dimensions = dimensions,
		};
		GeneratedEmbeddings<Embedding<float>> generated = await generator.GenerateAsync(inputs, options, cancellationToken);
		return generated.Select(embedding => embedding.Vector.ToArray()).ToList();
	}

	/// <summary>使内存分布式缓存失效；旧条目保留到进程结束但不会再次命中。</summary>
	public void ClearCache()
	{
		lock (_gate)
		{
			_cacheEpoch++;
			_state = null;
		}
	}

	private IEmbeddingGenerator<string, Embedding<float>> GetGenerator(
		string baseUrl,
		string apiKey,
		string model,
		int? dimensions,
		string fingerprint)
	{
		lock (_gate)
		{
			if (_state is {Fingerprint: var current} && current == fingerprint) return _state.Generator;

			OpenAIClientOptions options = new()
			{
				Endpoint = new Uri($"{baseUrl}/"),
				Transport = new HttpClientPipelineTransport(_httpClient),
			};
			EmbeddingClient client = new(model, new ApiKeyCredential(apiKey ?? ""), options);
			IEmbeddingGenerator<string, Embedding<float>> inner = client.AsIEmbeddingGenerator(dimensions);
			MemoryDistributedCache cache = new(Options.Create(new MemoryDistributedCacheOptions {SizeLimit = CacheSizeLimit}));
			DistributedCachingEmbeddingGenerator<string, Embedding<float>> cached =
				new(inner, cache)
				{
					CacheKeyAdditionalValues = [fingerprint, _cacheEpoch],
				};
			_state = new GeneratorState(fingerprint, cached);
			return cached;
		}
	}

	private static string NormalizeBaseUrl(string baseUrl)
	{
		string normalized = baseUrl.Trim().TrimEnd('/');
		if (normalized.EndsWith("/embeddings", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized[..^"/embeddings".Length];
		}
		if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
		{
			throw new InvalidOperationException("Embedding API Base URL 必须是绝对 HTTP(S) 地址");
		}
		return normalized;
	}

	private static string Fingerprint(string baseUrl, string apiKey, string model, int? dimensions)
	{
		string input = $"{baseUrl}\n{apiKey}\n{model}\n{dimensions?.ToString() ?? ""}";
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
	}

	private sealed record GeneratorState(string Fingerprint, IEmbeddingGenerator<string, Embedding<float>> Generator);
}
