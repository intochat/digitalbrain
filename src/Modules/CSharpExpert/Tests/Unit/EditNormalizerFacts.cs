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

    [Fact]
    public void RelativePathsResolveAgainstTheSolutionFolder()
    {
        var solutionFolder = Path.GetFullPath("solution-root");
        var edits = EditNormalizer.ResolvePaths(
            [new EditRequest(EditKind.AddUsing, Path: "SampleInbox.Tests/InboxTests.cs", Namespace: "SampleInbox"),
             new EditRequest(EditKind.AddUsing, Path: Path.Combine(solutionFolder, "Inbox.cs"), Namespace: "System")],
            solutionFolder);

        Assert.Equal(Path.Combine(solutionFolder, "SampleInbox.Tests", "InboxTests.cs"), edits[0].Path);
        Assert.Equal(Path.Combine(solutionFolder, "Inbox.cs"), edits[1].Path);
    }
}
