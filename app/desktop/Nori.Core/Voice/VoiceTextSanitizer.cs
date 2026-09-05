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
			// 跳过 emoji（代理对 / 变体选择符 / ZWJ），IndexTTS 会读成怪叫
			if (IsEmojiAt(text, i))
			{
				i += EmojiLength(text, i);
				continue;
			}

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

	/// <summary>判断位置 i 是否处于一个 emoji 序列的起点（代理对 / 变体选择符 / ZWJ / 常见 emoji 符号）。</summary>
	private static bool IsEmojiAt(string text, int i)
	{
		char c = text[i];
		// 补充平面（U+10000 以上）：代理对，基本全是 emoji / 罕见表意字，TTS 语境按 emoji 处理
		if (char.IsHighSurrogate(c)) return true;
		// 低代理不应出现在这里（前面已被 HighSurrogate 消费），保险起见也跳过
		if (char.IsLowSurrogate(c)) return true;
		// 变体选择符 VS16（U+FE0F，emoji 形式）与 VS15（U+FE0E）
		if (c == '\uFE0F' || c == '\uFE0E') return true;
		// ZWJ 零宽连接符（多 emoji 组合，如 👨👩👧）
		if (c == '\u200D') return true;
		// 杂项符号区中常见 emoji（U+2600–U+27BF 子集）
		return EmojiSymbol.Contains(c);
	}

	/// <summary>从位置 i 起跳过整个 emoji 序列（含后续变体选择符/ZWJ 组合），返回序列长度。</summary>
	private static int EmojiLength(string text, int i)
	{
		int count = 0;
		while (i + count < text.Length)
		{
			char c = text[i + count];
			if (char.IsHighSurrogate(c))
			{
				if (i + count + 1 < text.Length && char.IsLowSurrogate(text[i + count + 1]))
				{
					count += 2;
					continue;
				}
				count++;
				continue;
			}
			if (char.IsLowSurrogate(c) || c == '\uFE0F' || c == '\uFE0E' || c == '\u200D')
			{
				count++;
				continue;
			}
			if (EmojiSymbol.Contains(c))
			{
				count++;
				continue;
			}
			break;
		}
		return Math.Max(1, count);
	}

	/// <summary>杂项符号/装饰符号区中常见的 emoji 字符（单 char 表示的 emoji）。</summary>
	private static readonly HashSet<char> EmojiSymbol =
	[
		'\u00A9', // ©
		'\u00AE', // ®
		'\u203C', // ‼
		'\u2049', // ⁉
		'\u2122', // ™
		'\u2139', // ℹ
		'\u2194', '\u2195', '\u2196', '\u2197', '\u2198', '\u2199', // ↔ ↕ ↖ ↗ ↘ ↙
		'\u21A9', '\u21AA', // ↩ ↪
		'\u231A', '\u231B', // ⌚ ⌛
		'\u2328', // ⌨
		'\u23CF', '\u23E9', '\u23EA', '\u23EB', '\u23EC', '\u23ED', '\u23EE', '\u23EF', '\u23F0', '\u23F1', '\u23F2', '\u23F3', // ⏏ ⏩ ⏪ ⏫ ⏬ ⏭ ⏮ ⏯ ⏰ ⏱ ⏲ ⏳
		'\u23F8', '\u23F9', '\u23FA', // ⏸ ⏹ ⏺
		'\u24C2', // Ⓜ
		'\u25AA', '\u25AB', '\u25B6', '\u25C0', '\u25FB', '\u25FC', '\u25FD', '\u25FE', // ▪ ▫ ▶ ◀ ◻ ◼ ◽ ◾
		'\u2600', '\u2601', '\u2602', '\u2603', '\u2604', '\u260E', '\u2611', '\u2614', '\u2615', // ☀ ☁ ☂ ☃ ☄ ☎ ☑ ☔ ☕
		'\u2618', '\u261D', '\u2620', '\u2622', '\u2623', '\u2626', '\u262A', '\u262E', '\u262F', '\u2638', '\u2639', '\u263A', // ☘ ☝ ☠ ☢ ☣ ☦ ☪ ☮ ☯ ☸ ☹ ☺
		'\u2640', '\u2642', '\u2648', '\u2649', '\u264A', '\u264B', '\u264C', '\u264D', '\u264E', '\u264F', '\u2650', '\u2651', '\u2652', '\u2653', // ♀ ♂ 十二星座
		'\u2660', '\u2663', '\u2665', '\u2666', '\u2668', // ♠ ♣ ♥ ♦ ♨
		'\u267B', '\u267E', '\u267F', // ♻ ♾ ♿
		'\u2692', '\u2693', '\u2694', '\u2696', '\u2697', '\u2699', '\u269B', '\u269C', '\u26A0', '\u26A1', '\u26A7', '\u26AA', '\u26AB', // ⚒ ⚓ ⚔ ⚖ ⚗ ⚙ ⚛ ⚜ ⚠ ⚡ ⚧ ⚪ ⚫
		'\u26B0', '\u26B1', '\u26BD', '\u26BE', // ⚰ ⚱ ⚽ ⚾
		'\u26C4', '\u26C5', '\u26C8', '\u26CE', '\u26CF', '\u26D1', '\u26D3', '\u26D4', '\u26E9', '\u26EA', '\u26F0', '\u26F1', '\u26F2', '\u26F3', '\u26F4', '\u26F5', '\u26F7', '\u26F8', '\u26F9', '\u26FA', '\u26FD', // ⛄ ⛅ ⛈ ⛎ ⛏ ⛑ ⛓ ⛔ ⛩ ⛪ ⛰ ⛱ ⛲ ⛳ ⛴ ⛵ ⛷ ⛸ ⛹ ⛺ ⛽
		'\u2702', '\u2705', '\u2708', '\u2709', '\u270A', '\u270B', '\u270C', '\u270D', '\u270F', '\u2712', '\u2714', '\u2716', '\u271D', '\u2721', // ✂ ✅ ✈ ✉ ✊ ✋ ✌ ☝ ✍ ✏ ✒ ✔ ✖ ✝ ✡
		'\u2728', '\u2733', '\u2734', '\u2744', '\u2747', '\u274C', '\u274E', '\u2753', '\u2754', '\u2755', '\u2757', // ✨ ✳ ✴ ❄ ❇ ❌ ❎ ❓ ❔ ❕ ❗
		'\u2763', '\u2764', '\u2795', '\u2796', '\u2797', '\u27A1', '\u27B0', '\u27BF', // ❣ ❤ ➕ ➖ ➗ ➡ ➰ ➿
	];

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
