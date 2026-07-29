using DigitalBrain.AI;
using Xunit;

namespace DigitalBrain.OS.Behaviors.Tests;

public sealed class ModelMentionScanning
{
    private const string Gemma = "Gemma4";
    private const string Llama = "Llama32";
    private const string Granite = "Granite41";
    private const string Gpt = "Gpt56";

    [Theory(DisplayName = "a model is named by its bare family, by its full name, and in any casing")]
    [InlineData("compare Gemma with something")]
    [InlineData("compare Gemma4 with something")]
    [InlineData("compare gemma4 with something")]
    [InlineData("compare GEMMA with something")]
    [InlineData("`Gemma4` is fenced")]
    [InlineData("<Gemma4> is bracketed")]
    [InlineData("Gemma4")]
    [InlineData("what about Gemma?")]
    public void AModelIsNamedByFamilyByFullNameAndInAnyCasing(string text)
        => Assert.Equal([Gemma], ModelMentions.NamedIn(text));

    [Theory(DisplayName = "a run of letters or digits touching the name is a different word, not a model")]
    [InlineData("IGemma4 is the contract")]
    [InlineData("Gemmatology is not a model")]
    [InlineData("xGemma")]
    [InlineData("Gemma4x")]
    [InlineData("4Gemma")]
    [InlineData("")]
    [InlineData("Hi")]
    public void ALetterOrDigitRunTouchingTheNameIsADifferentWord(string text)
        => Assert.Empty(ModelMentions.NamedIn(text));

    [Theory(DisplayName = "a digit suffix that identifies no model this build knows is not a model mention")]
    [InlineData("compare Gemma9 with something")]
    [InlineData("compare Gemma44 with something")]
    public void ADigitSuffixThatIdentifiesNoKnownModelIsNotAMention(string text)
        => Assert.Empty(ModelMentions.NamedIn(text));

    [Fact(DisplayName = "one model named twice, bare and with its digits, is counted once")]
    public void OneModelNamedTwiceIsCountedOnce()
        => Assert.Equal([Gemma], ModelMentions.NamedIn("is Gemma the same as Gemma4?"));

    [Fact(DisplayName = "two models of different families are both named, in roster order")]
    public void TwoModelsOfDifferentFamiliesAreBothNamed()
        => Assert.Equal([Gemma, Llama], ModelMentions.NamedIn("compare Gemma and Llama on this"));

    [Fact(DisplayName = "families sharing a first letter stay distinct models")]
    public void FamiliesSharingAFirstLetterStayDistinct()
        => Assert.Equal([Gpt, Granite], ModelMentions.NamedIn("weigh Gpt56 against Granite41"));

    [Fact(DisplayName = "every model this build knows is reachable by its own name")]
    public void EveryKnownModelIsReachableByItsOwnName()
        => Assert.All(
            TeamLineUp.KnownModels,
            model => Assert.Equal([model], ModelMentions.NamedIn($"tell me about {model}")));
}
