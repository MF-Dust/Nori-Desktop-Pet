using Nori.Core.Agent;

namespace Nori.Core.Tests;

public sealed class PetInteractionReactionParserTests
{
	[Fact]
	public void ParsesStrictJsonReaction()
	{
		PetInteractionReaction result = PetInteractionReactionParser.Parse("{\"text\":\"呀\",\"emotion\":\"shy\",\"expression\":\"04_Shy\",\"action\":\"02_Nod\"}");

		Assert.Equal("呀", result.Text);
		Assert.Equal("shy", result.Emotion);
		Assert.Equal("04_Shy", result.Expression);
		Assert.Equal("02_Nod", result.Motion);
	}

	[Fact]
	public void ParsesJsonCodeFence()
	{
		PetInteractionReaction result = PetInteractionReactionParser.Parse("```json\n{\"text\":\"你好\"}\n```");

		Assert.Equal("你好", result.Text);
	}

	[Fact]
	public void UnknownFieldsAreIgnored()
	{
		PetInteractionReaction result = PetInteractionReactionParser.Parse("{\"text\":\"好\",\"unknown\":123}");

		Assert.Equal("好", result.Text);
	}

	[Fact]
	public void ToolCallIsRejected()
	{
		Assert.Throws<InvalidOperationException>(() => PetInteractionReactionParser.Parse(
			"{\"text\":\"好\",\"tool_call\":{\"name\":\"shell\"}}"));
	}

	[Fact]
	public void OversizedTextIsRejected()
	{
		string text = new('a', PetInteractionReactionParser.MaxTextLength + 1);

		Assert.Throws<InvalidOperationException>(() => PetInteractionReactionParser.Parse($"{{\"text\":\"{text}\"}}"));
	}

	[Theory]
	[InlineData("")]
	[InlineData("not json")]
	[InlineData("[1, 2]")]
	public void InvalidResponseIsRejected(string raw)
	{
		Assert.Throws<InvalidOperationException>(() => PetInteractionReactionParser.Parse(raw));
	}
}
