using System.Text;

namespace Nori.Core.Voice;

/// <summary>
/// TTS 文本清洗：合成前移除会被读出来的颜文字 / 装饰符号。
///
/// 与 IndexTTS 技能（tts_cli）的 strip_kaomoji 行为保持一致：
/// 1. 括号组：内容含「颜文字专属字符」（假名/泰文/谚文/希腊字母/特殊符号等），
///    或纯符号、或 ASCII 表情（(^_^)/(T_T)），连同前导 ╭╰╮╯ 和尾随装饰一起删除；
/// 2. 文本末尾独立的硬装饰字符（✧❤♪ 等）删除；～~ 是语气保留。
///
/// 不误伤正常括号：(开心)、（重要）、(メニュー)、(x86) 原样保留。
/// </summary>
public static class VoiceTextSanitizer
{
	/// <summary>括号内容里出现这些字符即判定为颜文字组。</summary>
	private static readonly HashSet<char> KaomojiInside =
	[
		// 泰文/谚文
		'๑', '๒', 'ㅂ', 'ㅋ', 'ㅎ', 'ㅇ',
		// 横线/项目符号
		'￣', '￢', '•',
		// 假名系
		'づ', 'ヅ', 'ノ', 'ヽ', 'ﾉ', 'ゞ', 'ヾ', 'ゝ',
		// 希腊系
		'ω', 'Ω', 'Σ', 'ψ',
		// 度数符号
		'°', 'º',
		// 半角句点
		'｡',
		// 几何/装饰
		'◕', '◜', '◝', '◞', '◟', '◠', '◡', '◉', '◎', '⊙', '☉', '◍', '●', '○', '◐', '◑', '△', '▽', '◇', '□', '■', '☆', '★', '✰',
		// 拟声/嘴角
		'˘', 'ς', 'ᴗ', 'ʖ', 'ʕ', 'ʔ', 'ˁ',
		// 心
		'❤', '♡', '♥', '❥', '❣',
		// 星/闪
		'✧', '✦', '✨', '✩', '✪', '✫', '✬', '❀', '❁',
		// 波浪点
		'ﾟ', '･', '゜', '゚',
		// 括号表意
		'┻', '━', '┬', '╯', '╰', '╮', '╭', '┌', '└',
		// 上下括号
		'₍', '₎',
		// 重音/变音
		'´', 'ˋ', '`', '̀', '̂', '̃', '̑',
		// 比较/任意
		'≧', '≦', '∀',
		// 方块
		'▰', '▱',
		// 阿拉伯/波斯
		'و', '٦', '٧', '٨', '٩', '۶',
	];

	/// <summary>括号前紧贴的前导装饰（如 ╮(￣▽￣)╭ 的 ╮）。</summary>
	private static readonly HashSet<char> LeadDecor = ['╭', '╰', '╮', '╯'];

	/// <summary>括号后紧贴的装饰字符（颜文字的一部分，一起删除）。</summary>
	private static readonly HashSet<char> TrailDecor =
	[
		'✧', '✦', '✨', '❤', '♡', '♥', '❥', '❣', '☆', '★', '✩', '✪', '✫', '✬', '❀', '❁',
		'ﾟ', '゜', '゚', '･', '・', 'ヽ', 'ヾ', 'ﾉ', 'ノ', 'づ', 'ヅ', 'ゝ', 'ゞ', '╮', '╯', '╭', '╰', 'ヮ',
		'و', '٦', '٧', '٨', '٩', '۶',
		'*', '～', '~', '︵', '┻', '━', '┬',
	];

	/// <summary>纯符号括号组允许的字符（(・_・)、(｡･ω･｡) 这类）。</summary>
	private static readonly HashSet<char> PunctOnly = ['・', '_', '｡', '･', 'ﾟ', '゜', '＊', '*', ' ', '\t'];

	/// <summary>ASCII 表情符号允许的字符（(^_^)、(T_T)、(>_&lt;) 这类）。</summary>
	private static readonly HashSet<char> AsciiEmoticon =
		['=', '^', '>', '<', ';', ':', '_', 'o', 'O', '0', 'v', 'V', '*', '.', ',', '\'', '/', '~', 'T', 't', '0', '-'];

	/// <summary>文本末尾独立的硬装饰字符（删除；～~ 是语气，不在此列）。</summary>
	private static readonly HashSet<char> TrailingHard =
	[
		'✧', '✦', '✨', '❤', '♡', '♥', '❥', '❣', '☆', '★', '✩', '✪', '✫', '✬', '❀', '❁',
		'ﾟ', '゜', '゚', '･', '・', '♪', '♫', '♬',
	];

	/// <summary>移除文本中的颜文字/装饰符号，返回清洗后的文本。空串原样返回。</summary>
	public static string StripKaomoji(string? text)
	{
		if (string.IsNullOrEmpty(text)) return text ?? "";

		var builder = new StringBuilder(text.Length);
		int i = 0;
		int length = text.Length;

		while (i < length)
		{
			// 记录前导装饰起点（╭╰╮╯），若后续判定为颜文字则连同丢弃
			int leadStart = i;
			int cursor = i;
			while (cursor < length && LeadDecor.Contains(text[cursor])) cursor++;
			int contentStart = cursor;

			// 是否是开括号
			bool isOpen = cursor < length && (text[cursor] == '(' || text[cursor] == '（');
			if (!isOpen)
			{
				// 非括号：前导装饰是独立字符（保留），推进一个字符
				builder.Append(text, i, 1);
				i++;
				continue;
			}

			char openChar = text[cursor];
			char closeChar = openChar == '(' ? ')' : '）';
			int openIndex = cursor;

			// 扫描到闭括号（不跨行、不跨嵌套开括号）
			int scan = openIndex + 1;
			int contentEnd = -1;
			while (scan < length)
			{
				char c = text[scan];
				if (c == closeChar)
				{
					contentEnd = scan;
					break;
				}
				if (c == '\n' || c == '(' || c == '（') break;
				scan++;
			}

			if (contentEnd < 0)
			{
				// 无匹配闭括号：保留前导装饰 + 当前字符，推进一个
				builder.Append(text, leadStart, Math.Max(1, openIndex - leadStart + 1));
				i = leadStart + 1;
				continue;
			}

			string content = text.Substring(openIndex + 1, contentEnd - openIndex - 1);
			if (IsKaomojiContent(content))
			{
				// 颜文字：丢弃前导装饰 + 括号组 + 尾随装饰
				i = contentEnd + 1;
				while (i < length && TrailDecor.Contains(text[i])) i++;
			}
			else
			{
				// 正常括号：保留前导装饰 + 括号组
				builder.Append(text, leadStart, contentEnd - leadStart + 1);
				i = contentEnd + 1;
			}
		}

		string result = builder.ToString();
		return TrimTrailingHard(result);
	}

	/// <summary>判断括号内容是否属于颜文字（而非正常文本）。</summary>
	private static bool IsKaomojiContent(string content)
	{
		if (content.Length == 0) return false;
		foreach (char c in content)
		{
			if (KaomojiInside.Contains(c)) return true;
		}
		if (AllIn(content, PunctOnly)) return true;
		if (content.Length >= 2 && AllIn(content, AsciiEmoticon)) return true;
		return false;
	}

	private static bool AllIn(string content, HashSet<char> set)
	{
		foreach (char c in content)
		{
			if (!set.Contains(c)) return false;
		}
		return true;
	}

	/// <summary>移除文本末尾独立的硬装饰字符（含前面空格），保留 ～~ 语气波浪。</summary>
	private static string TrimTrailingHard(string text)
	{
		int end = text.Length;
		while (end > 0)
		{
			char c = text[end - 1];
			if (c == ' ' || c == '\t' || TrailingHard.Contains(c))
			{
				end--;
			}
			else
			{
				break;
			}
		}
		return end == text.Length ? text : text.Substring(0, end);
	}
}
