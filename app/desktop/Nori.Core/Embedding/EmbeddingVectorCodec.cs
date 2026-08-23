using System.Buffers.Binary;
using System.Text.Json;

namespace Nori.Core.Embedding;

/// <summary>Embedding 向量的 Float32 little-endian BLOB 与旧 JSON 转换。</summary>
public static class EmbeddingVectorCodec
{
	/// <summary>把有限的 Float32 向量编码成稳定的 little-endian BLOB。</summary>
	public static byte[] Encode(ReadOnlySpan<float> vector)
	{
		if (vector.Length == 0) throw new ArgumentException("向量不能为空", nameof(vector));
		byte[] bytes = new byte[checked(vector.Length * sizeof(float))];
		for (int index = 0; index < vector.Length; index++)
		{
			if (!float.IsFinite(vector[index])) throw new ArgumentException("向量必须全部为有限数", nameof(vector));
			BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(float), sizeof(float)), BitConverter.SingleToInt32Bits(vector[index]));
		}
		return bytes;
	}

	/// <summary>从 BLOB 解码到调用方提供的缓冲区，避免每个候选重复分配数组。</summary>
	public static bool TryDecode(ReadOnlySpan<byte> bytes, Span<float> destination, out int length)
	{
		length = 0;
		if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0) return false;
		int count = bytes.Length / sizeof(float);
		if (destination.Length < count) return false;
		for (int index = 0; index < count; index++)
		{
			float value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(index * sizeof(float), sizeof(float))));
			if (!float.IsFinite(value)) return false;
			destination[index] = value;
		}
		length = count;
		return true;
	}

	/// <summary>从 BLOB 解码成兼容旧 API 的数组。</summary>
	public static bool TryDecode(ReadOnlySpan<byte> bytes, out float[] vector)
	{
		vector = [];
		if (bytes.Length == 0 || bytes.Length % sizeof(float) != 0) return false;
		float[] result = new float[bytes.Length / sizeof(float)];
		if (!TryDecode(bytes, result, out int length) || length != result.Length) return false;
		vector = result;
		return true;
	}

	/// <summary>把旧 JSON 数组直接解析到调用方缓冲区。</summary>
	public static bool TryDecodeJson(string json, Span<float> destination, out int length)
	{
		length = 0;
		if (string.IsNullOrWhiteSpace(json)) return false;
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > destination.Length) return false;
			foreach (JsonElement element in document.RootElement.EnumerateArray())
			{
				if (!element.TryGetSingle(out float value) || !float.IsFinite(value)) return false;
				destination[length++] = value;
			}
			return length > 0;
		}
		catch (JsonException)
		{
			length = 0;
			return false;
		}
	}

	/// <summary>从旧 JSON 解码成兼容旧 API 的数组。</summary>
	public static bool TryDecodeJson(string json, out float[] vector)
	{
		vector = [];
		if (string.IsNullOrWhiteSpace(json)) return false;
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
			float[] result = new float[document.RootElement.GetArrayLength()];
			if (!TryDecodeJson(json, result, out int length) || length != result.Length) return false;
			vector = result;
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	/// <summary>为旧的 MemoryItem.Embedding 兼容字段生成 JSON 表示。</summary>
	public static string ToJson(ReadOnlySpan<float> vector) => JsonSerializer.Serialize(vector.ToArray());
}
