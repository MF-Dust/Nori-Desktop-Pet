using System.Text.RegularExpressions;
using Nori.Core.Agent;

namespace Nori.Core.Tests;

/// <summary>
/// 流式协议解析器用例, 与前端 tests/agent/json-parser.test.ts 对齐
/// </summary>
public class StreamingJsonParserTests
{
	[Fact]
	public void 分片流式提取完整JSON()
	{
		StreamingJsonParser parser = new();
		string chunk1 = "```json\n{\"type\": \"message\", \"text\": \"你好呀";
		string chunk2 = "主人！\", \"emotion\": \"happy\", \"action\": \"smile\"}\n```";

		Assert.Empty(parser.Push(chunk1));
		IReadOnlyList<AgentProtocolItem> results = parser.Push(chunk2);

		ProtocolMessage message = Assert.IsType<ProtocolMessage>(results.Single());
		Assert.Equal("你好呀主人！", message.Text);
		Assert.Equal("happy", message.Emotion);
		Assert.Equal("smile", message.Action);
	}

	[Fact]
	public void 解析工具调用tool_call()
	{
		StreamingJsonParser parser = new();
		var results = parser.Push("{\"type\": \"tool_call\", \"id\": \"c1\", \"name\": \"getTime\", \"arguments\": {}}");

		ProtocolToolCall call = Assert.IsType<ProtocolToolCall>(results.Single());
		Assert.Equal("getTime", call.Name);
		Assert.NotNull(call.Arguments);
	}

	[Fact]
	public void tool_call的arguments为JSON字符串时二次解析()
	{
		StreamingJsonParser parser = new();
		var results = parser.Push("{\"type\": \"tool_call\", \"name\": \"getWeather\", \"arguments\": \"{\\\"city\\\": \\\"北京\\\"}\"}");

		ProtocolToolCall call = Assert.IsType<ProtocolToolCall>(results.Single());
		Assert.Equal("getWeather", call.Name);
		Assert.Equal("北京", call.Arguments?["city"]?.GetValue<string>());
	}

	[Fact]
	public void 解析事件类型event()
	{
		StreamingJsonParser parser = new();
		var results = parser.Push("{\"type\": \"event\", \"name\": \"pet-motion\", \"payload\": {\"name\": \"wave\"}}");

		ProtocolEvent evt = Assert.IsType<ProtocolEvent>(results.Single());
		Assert.Equal("pet-motion", evt.Name);
		Assert.Equal("wave", evt.Payload?["name"]?.GetValue<string>());
	}

	[Fact]
	public void 非JSON格式普通文本兜底()
	{
		StreamingJsonParser parser = new();
		parser.Push("这是一条未格式化的纯文本助手回复。");
		var flushed = parser.Flush();

		ProtocolMessage message = Assert.IsType<ProtocolMessage>(flushed.Single());
		Assert.Equal("这是一条未格式化的纯文本助手回复。", message.Text);
	}

	[Fact]
	public void l2dAction兼容别名映射到action()
	{
		StreamingJsonParser parser = new();
		var results = parser.Push("{\"type\": \"message\", \"text\": \"hi\", \"l2dAction\": \"wave\"}");

		ProtocolMessage message = Assert.IsType<ProtocolMessage>(results.Single());
		Assert.Equal("wave", message.Action);
	}

	[Fact]
	public void 静态ParseComplete直接解析完整输出()
	{
		var results = StreamingJsonParser.ParseComplete("```json\n{\"type\": \"message\", \"text\": \"ok\"}\n```");

		ProtocolMessage message = Assert.IsType<ProtocolMessage>(results.Single());
		Assert.Equal("ok", message.Text);
	}

	[Fact]
	public void 逐字符小chunk的对象只产出一次且结果正确()
	{
		StreamingJsonParser parser = new();
		const string FULL = "{\"type\": \"message\", \"text\": \"你好呀主人！\", \"emotion\": \"happy\", \"action\": \"smile\"}";

		List<AgentProtocolItem> collected = [];
		foreach (char ch in FULL)
		{
			collected.AddRange(parser.Push(ch.ToString()));
		}
		ProtocolMessage message = Assert.IsType<ProtocolMessage>(collected.Single());
		Assert.Equal("你好呀主人！", message.Text);
		Assert.Equal("happy", message.Emotion);
		Assert.Equal("smile", message.Action);
	}

	[Fact]
	public void 转义引号跨chunk时字符串状态保持正确()
	{
		StreamingJsonParser parser = new();
		const string CHUNK1 = "{\"type\": \"tool_call\", \"name\": \"run\", \"arguments\": {\"cmd\": \"echo \\\"hello";
		const string CHUNK2 = " world\\\"\"}}";

		Assert.Empty(parser.Push(CHUNK1));
		var results = parser.Push(CHUNK2);

		ProtocolToolCall call = Assert.IsType<ProtocolToolCall>(results.Single());
		Assert.Equal("echo \"hello world\"", call.Arguments?["cmd"]?.GetValue<string>());
	}

	[Fact]
	public void 连续对象在同一调用内全部输出()
	{
		StreamingJsonParser parser = new();
		var results = parser.Push(
			"{\"type\": \"message\", \"text\": \"a\"}{\"type\": \"event\", \"name\": \"e1\"}" +
			"{\"type\": \"tool_call\", \"id\": \"c9\", \"name\": \"t\", \"arguments\": {}}");

		Assert.Equal(["message", "event", "tool_call"], results.Select(item =>
			item switch
			{
				ProtocolMessage => "message",
				ProtocolEvent => "event",
				ProtocolToolCall => "tool_call",
				_ => "?",
			}));
	}

	[Fact]
	public void 非法平衡对象后仍能继续解析后续对象()
	{
		StreamingJsonParser parser = new();
		Assert.Empty(parser.Push("{oops: 1}"));
		var second = parser.Push("{\"type\": \"message\", \"text\": \"ok\"}");
		Assert.Single(second);
		var flushed = parser.Flush();
		// 与旧实现一致: 后续成功解析会连同之前的非法平衡段一起消费
		Assert.Empty(flushed);
	}

	[Fact]
	public void 超限的未闭合payload抛出可处理错误()
	{
		StreamingJsonParser parser = new(32);
		parser.Push("{\"type\": \"message\", \"text\": \"");
		Assert.Throws<InvalidOperationException>(() => parser.Push(new string('x', 64)));
	}
}
