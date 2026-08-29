namespace Nori.Core.Chat.Adapters;

/// <summary>模型目录适配器: 仅负责按厂商协议拉取可用模型列表。</summary>
public interface IModelCatalogAdapter
{
	/// <summary>获取支持的模型列表</summary>
	Task<IReadOnlyList<string>> FetchModelsAsync(
		string baseUrl,
		string apiKey,
		CancellationToken cancellationToken = default);
}
