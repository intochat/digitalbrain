using DigitalBrain.CSharpExpert;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class DiffReviewFacts
{
    [Fact]
    public void ACleanDiffHasNoFindings()
        => Assert.Empty(DiffReview.Check("""
            --- a/Inbox.cs
            +++ b/Inbox.cs
            @@ -1,1 +1,2 @@
            +    public void Clear() => _messages.Clear();
            """));

    [Fact]
    public void AnAddedSummaryCommentIsAFinding()
    {
        var findings = DiffReview.Check("""
            +++ b/Inbox.cs
            +    /// <summary>
            +    public void Clear() => _messages.Clear();
            """);

        Assert.Contains(findings, finding => finding.Contains("doc comment", StringComparison.Ordinal));
    }

    [Fact]
    public void ARemovedSummaryCommentIsNotAFinding()
        => Assert.Empty(DiffReview.Check("""
            -    /// <summary>
            +    public void Clear() => _messages.Clear();
            """));

    [Fact]
    public void DenseInlineCommentsAreAFinding()
    {
        var findings = DiffReview.Check("""
            +    // clear
            +    // the
            +    // messages
            +    public void Clear() => _messages.Clear();
            """);

        Assert.Contains(findings, finding => finding.Contains("inline comments", StringComparison.Ordinal));
    }

    [Fact]
    public void CrypticLocalNamesAreFindingsButLoopIndexesAreNot()
    {
        var findings = DiffReview.Check("""
            +        var ms = _messages.Count;
            +        for (int i = 0; i < ms; i++) { }
            """);

        Assert.Equal(["Rename 'ms' to a self-explanatory name."], findings);
    }
}
