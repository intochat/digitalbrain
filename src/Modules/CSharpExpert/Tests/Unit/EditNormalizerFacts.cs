using DigitalBrain.CSharpExpert;
using DigitalBrain.Microsoft.Roslyn;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class EditNormalizerFacts
{
    [Theory]
    [InlineData("M:SampleInbox.Inbox.Clear", "T:SampleInbox.Inbox")]
    [InlineData("M:SampleInbox.Inbox.Add(System.String)", "T:SampleInbox.Inbox")]
    [InlineData("P:SampleInbox.Inbox.Count", "T:SampleInbox.Inbox")]
    [InlineData("T:SampleInbox.Inbox", "T:SampleInbox.Inbox")]
    public void AnInsertTargetsTheContainingType(string symbolId, string expected)
    {
        var edit = Assert.Single(EditNormalizer.Normalize([new EditRequest(EditKind.InsertMember, SymbolId: symbolId, Source: "public void Clear() { }")]));

        Assert.Equal(expected, edit.SymbolId);
    }

    [Fact]
    public void AReplaceKeepsItsMemberId()
    {
        var edit = Assert.Single(EditNormalizer.Normalize([new EditRequest(EditKind.ReplaceMember, SymbolId: "M:SampleInbox.Inbox.Add(System.String)", Source: "x")]));

        Assert.Equal("M:SampleInbox.Inbox.Add(System.String)", edit.SymbolId);
    }
}
