using System.Globalization;
using System.Text;

namespace Nori.Core.Agent;

/// <summary>一次流式文本投影的结果。</summary>
public sealed record StreamingTextProjection(
	string Delta,
	string FullText,
	bool IsCorrection,
	bool IsComplete);

/// <summary>
/// 从尚未闭合的 Nori message JSON 中投影可见的 text 字段。
///
/// 该类只负责展示文本，不负责决定协议对象是否完整；流结束后必须使用完整解析器的结果调用
/// <see cref="Complete"/>，这样字段缺失、转义错误或模型中途改写内容时，最终消息仍以完整解析为准。
/// </summary>
public sealed class StreamingMessageTextProjector
{
	/// <summary>单次投影缓冲区上限 (字符数)</summary>
	public const int DefaultMaxBufferCharacters = 1_000_000;

	private readonly int _maxBufferCharacters;
	private readonly StringBuilder _raw = new();
	private string _projected = "";

	public StreamingMessageTextProjector(int maxBufferCharacters = DefaultMaxBufferCharacters)
	{
		if (maxBufferCharacters <= 0) throw new ArgumentOutOfRangeException(nameof(maxBufferCharacters));
		_maxBufferCharacters = maxBufferCharacters;
	}

	/// <summary>当前已经投影的完整可见文本。</summary>
	public string CurrentText => _projected;

	/// <summary>是否已经收到过可见文本。</summary>
	public bool HasProjectedText => _projected.Length > 0;

	/// <summary>
	/// 追加一个任意边界的协议分片。返回值不会重复之前已经投影的字符。
	/// </summary>
	public StreamingTextProjection Push(string chunk)
	{
		ArgumentNullException.ThrowIfNull(chunk);
		if (chunk.Length == 0) return new StreamingTextProjection("", _projected, false, false);
		if (_raw.Length + chunk.Length > _maxBufferCharacters)
		{
			Reset();
			throw new InvalidOperationException($"流式消息投影缓冲区超过上限 ({_maxBufferCharacters} 字符)，已终止当前输入");
		}

		_raw.Append(chunk);
		string visible = ScanVisibleText(_raw.ToString());
		return ApplyVisibleText(visible, isComplete: false);
	}

	/// <summary>
	/// 使用完整协议解析器的结果修正投影。完整解析结果始终拥有最高优先级。
	/// </summary>
	public StreamingTextProjection Complete(IReadOnlyList<AgentProtocolItem> parsedItems)
	{
		ArgumentNullException.ThrowIfNull(parsedItems);
		string visible = string.Join("\n", parsedItems
			.OfType<ProtocolMessage>()
			.Where(message => message.Text.Length > 0)
			.Select(message => message.Text));
		StreamingTextProjection result = ApplyVisibleText(visible, isComplete: true);
		Reset();
		return result;
	}

	/// <summary>清除当前投影状态。</summary>
	public void Reset()
	{
		_raw.Clear();
		_projected = "";
	}

	private StreamingTextProjection ApplyVisibleText(string visible, bool isComplete)
	{
		if (visible.StartsWith(_projected, StringComparison.Ordinal))
		{
			string delta = visible[_projected.Length..];
			_projected = visible;
			return new StreamingTextProjection(delta, visible, false, isComplete);
		}

		if (string.Equals(visible, _projected, StringComparison.Ordinal))
		{
			return new StreamingTextProjection("", visible, false, isComplete);
		}

		// 已经发出的文本无法通过追加回调撤回，因此把不一致标记为 correction，交给上层
		// 通过替换语义处理；绝不能把完整文本再次当作增量发送，避免重复显示。
		_projected = visible;
		return new StreamingTextProjection("", visible, true, isComplete);
	}

	private static string ScanVisibleText(string raw)
	{
		StringBuilder messages = new();
		int cursor = 0;
		bool foundObject = false;
		while (cursor < raw.Length)
		{
			int start = raw.IndexOf('{', cursor);
			if (start < 0) break;
			foundObject = true;
			ObjectScan scan = ScanObject(raw, start);
			if (scan.Text is not null)
			{
				if (messages.Length > 0) messages.Append('\n');
				messages.Append(scan.Text);
			}
			if (!scan.Complete) break;
			cursor = Math.Max(start + 1, scan.NextIndex);
		}

		if (messages.Length > 0 || foundObject) return messages.ToString();

		// 与 StreamingJsonParser 的普通文本兜底保持一致，但代码块标记不能泄漏到 UI。
		string trimmed = raw.TrimStart();
		if (trimmed.StartsWith("```", StringComparison.Ordinal)) return "";
		return raw.Trim();
	}

	private static ObjectScan ScanObject(string raw, int start)
	{
		int index = start + 1;
		while (true)
		{
			SkipWhitespace(raw, ref index);
			if (index >= raw.Length) return new ObjectScan(null, false, raw.Length);
			if (raw[index] == '}') return new ObjectScan(null, true, index + 1);
			if (raw[index] != '"') return new ObjectScan(null, false, raw.Length);

			if (!TryReadJsonString(raw, index, out string key, out int afterKey, out bool keyComplete) || !keyComplete)
			{
				return new ObjectScan(null, false, raw.Length);
			}
			index = afterKey;
			SkipWhitespace(raw, ref index);
			if (index >= raw.Length || raw[index] != ':') return new ObjectScan(null, false, raw.Length);
			index++;
			SkipWhitespace(raw, ref index);
			if (index >= raw.Length) return new ObjectScan(key == "text" ? "" : null, false, raw.Length);

			if (key == "text" && raw[index] == '"')
			{
				if (!TryReadJsonString(raw, index, out string text, out int afterText, out bool textComplete))
				{
					return new ObjectScan(null, false, raw.Length);
				}
				if (!textComplete) return new ObjectScan(text, false, raw.Length);
				index = afterText;
				SkipWhitespace(raw, ref index);
				if (index >= raw.Length) return new ObjectScan(text, false, raw.Length);
				if (raw[index] == ',')
				{
					// 继续读后续属性，保证对象完整性仍然由完整解析器决定。
					return ContinueAfterValue(raw, index, text);
				}
				if (raw[index] == '}') return new ObjectScan(text, true, index + 1);
				return new ObjectScan(text, false, raw.Length);
			}

			if (!TrySkipJsonValue(raw, index, out int afterValue))
			{
				return new ObjectScan(null, false, raw.Length);
			}
			index = afterValue;
			SkipWhitespace(raw, ref index);
			if (index >= raw.Length) return new ObjectScan(null, false, raw.Length);
			if (raw[index] == ',')
			{
				index++;
				continue;
			}
			if (raw[index] == '}') return new ObjectScan(null, true, index + 1);
			return new ObjectScan(null, false, raw.Length);
		}
	}

	private static ObjectScan ContinueAfterValue(string raw, int commaIndex, string text)
	{
		int index = commaIndex + 1;
		while (true)
		{
			SkipWhitespace(raw, ref index);
			if (index >= raw.Length) return new ObjectScan(text, false, raw.Length);
			if (raw[index] == '}') return new ObjectScan(text, true, index + 1);
			if (raw[index] != '"') return new ObjectScan(text, false, raw.Length);
			if (!TryReadJsonString(raw, index, out _, out int afterKey, out bool complete) || !complete)
			{
				return new ObjectScan(text, false, raw.Length);
			}
			index = afterKey;
			SkipWhitespace(raw, ref index);
			if (index >= raw.Length || raw[index] != ':') return new ObjectScan(text, false, raw.Length);
			index++;
			SkipWhitespace(raw, ref index);
			if (index >= raw.Length) return new ObjectScan(text, false, raw.Length);
			if (!TrySkipJsonValue(raw, index, out int afterValue)) return new ObjectScan(text, false, raw.Length);
			index = afterValue;
			SkipWhitespace(raw, ref index);
			if (index >= raw.Length) return new ObjectScan(text, false, raw.Length);
			if (raw[index] == ',')
			{
				index++;
				continue;
			}
			if (raw[index] == '}') return new ObjectScan(text, true, index + 1);
			return new ObjectScan(text, false, raw.Length);
		}
	}

	private static bool TryReadJsonString(string raw, int start, out string value, out int nextIndex, out bool complete)
	{
		StringBuilder builder = new();
		char? pendingHighSurrogate = null;
		int index = start + 1;
		while (index < raw.Length)
		{
			char current = raw[index++];
			if (current == '"')
			{
				if (pendingHighSurrogate is { } high) builder.Append(high);
				value = builder.ToString();
				nextIndex = index;
				complete = true;
				return true;
			}
			if (current != '\\')
			{
				AppendCodeUnit(builder, ref pendingHighSurrogate, current);
				continue;
			}

			if (index >= raw.Length)
			{
				value = builder.ToString();
				nextIndex = raw.Length;
				complete = false;
				return true;
			}
			char escaped = raw[index++];
			switch (escaped)
			{
				case '"': AppendCodeUnit(builder, ref pendingHighSurrogate, '"'); break;
				case '\\': AppendCodeUnit(builder, ref pendingHighSurrogate, '\\'); break;
				case '/': AppendCodeUnit(builder, ref pendingHighSurrogate, '/'); break;
				case 'b': AppendCodeUnit(builder, ref pendingHighSurrogate, '\b'); break;
				case 'f': AppendCodeUnit(builder, ref pendingHighSurrogate, '\f'); break;
				case 'n': AppendCodeUnit(builder, ref pendingHighSurrogate, '\n'); break;
				case 'r': AppendCodeUnit(builder, ref pendingHighSurrogate, '\r'); break;
				case 't': AppendCodeUnit(builder, ref pendingHighSurrogate, '\t'); break;
				case 'u':
					if (raw.Length - index < 4)
					{
						value = builder.ToString();
						nextIndex = raw.Length;
						complete = false;
						return true;
					}
					if (!ushort.TryParse(raw.AsSpan(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort codeUnit))
					{
						value = builder.ToString();
						nextIndex = index;
						complete = false;
						return false;
					}
					index += 4;
					AppendCodeUnit(builder, ref pendingHighSurrogate, (char)codeUnit);
					break;
				default:
					value = builder.ToString();
					nextIndex = index;
					complete = false;
					return false;
			}
		}

		value = builder.ToString();
		nextIndex = raw.Length;
		complete = false;
		return true;
	}

	private static void AppendCodeUnit(StringBuilder builder, ref char? pendingHighSurrogate, char value)
	{
		if (pendingHighSurrogate is { } high)
		{
			if (char.IsLowSurrogate(value))
			{
				builder.Append(high);
				builder.Append(value);
				pendingHighSurrogate = null;
				return;
			}
			builder.Append(high);
			pendingHighSurrogate = null;
		}

		if (char.IsHighSurrogate(value))
		{
			pendingHighSurrogate = value;
		}
		else
		{
			builder.Append(value);
		}
	}

	private static bool TrySkipJsonValue(string raw, int start, out int nextIndex)
	{
		if (start >= raw.Length)
		{
			nextIndex = raw.Length;
			return false;
		}
		if (raw[start] == '"')
		{
			bool ok = TryReadJsonString(raw, start, out _, out nextIndex, out bool complete);
			return ok && complete;
		}
		if (raw[start] is '{' or '[')
		{
			char opening = raw[start];
			char closing = opening == '{' ? '}' : ']';
			int depth = 0;
			bool inString = false;
			bool escaped = false;
			for (int index = start; index < raw.Length; index++)
			{
				char current = raw[index];
				if (inString)
				{
					if (escaped) escaped = false;
					else if (current == '\\') escaped = true;
					else if (current == '"') inString = false;
					continue;
				}
				if (current == '"') { inString = true; continue; }
				if (current == opening) depth++;
				else if (current == closing && --depth == 0)
				{
					nextIndex = index + 1;
					return true;
				}
			}
			nextIndex = raw.Length;
			return false;
		}

		int cursor = start;
		while (cursor < raw.Length && raw[cursor] is not (',' or '}' or ']')) cursor++;
		if (cursor == raw.Length)
		{
			nextIndex = cursor;
			return false;
		}
		nextIndex = cursor;
		return cursor > start;
	}

	private static void SkipWhitespace(string text, ref int index)
	{
		while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
	}

	private sealed record ObjectScan(string? Text, bool Complete, int NextIndex);
}
