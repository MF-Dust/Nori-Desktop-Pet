using Nori.Core.Tools;

namespace Nori.Core.Tests;

/// <summary>
/// 数学表达式安全求值器用例, 对齐前端 evaluateMathExpression
/// </summary>
public class MathExpressionTests
{
	[Theory]
	[InlineData("1 + 2 * 3", 7)]
	[InlineData("(1 + 2) * 3", 9)]
	[InlineData("128 * 64", 8192)]
	[InlineData("2 ^ 10", 1024)]
	[InlineData("-5 + 3", -2)]
	[InlineData("15% * 200", 30)]
	[InlineData("sqrt(256)", 16)]
	[InlineData("sin(pi/2)", 1)]
	[InlineData("max(1, 5, 3)", 5)]
	[InlineData("pow(2, 3)", 8)]
	[InlineData("10 / 4", 2.5)]
	[InlineData("2**3", 8)]
	public void 计算表达式(string expression, double expected) =>
		Assert.Equal(expected, MathExpression.Calculate(expression), 9);

	[Theory]
	[InlineData("1 / 0", "除数不能为零")]
	[InlineData("10 % 0", "取模除数不能为零")]
	[InlineData("(1 + 2", "缺少匹配的闭括号")]
	[InlineData("foo(1)", "不支持的数学函数")]
	[InlineData("", "意外的表达式结尾")]
	[InlineData("1 @ 2", "无法识别的字符")]
	public void 非法表达式抛出可读错误(string expression, string reason)
	{
		FormatException error = Assert.Throws<FormatException>(() => MathExpression.Calculate(expression));
		Assert.Contains(reason, error.Message, StringComparison.Ordinal);
	}
}
