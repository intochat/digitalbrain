using DigitalBrain.Microsoft.Roslyn;

namespace DigitalBrain.Tests;

public sealed class WorkspaceFacts
{
    [Fact]
    public void IdenticalTextsProduceNoDiff()
        => Assert.Equal(string.Empty, LineDiff.Render("Sample.cs", "a\nb\n", "a\nb\n"));

    [Fact]
    public void ChangedLinesProduceAUnifiedHunk()
    {
        var diff = LineDiff.Render("Sample.cs", "a\nb\nc\n", "a\nB\nc\n");
        Assert.Contains("--- a/Sample.cs", diff);
        Assert.Contains("+++ b/Sample.cs", diff);
        Assert.Contains("@@ -2,1 +2,1 @@", diff);
        Assert.Contains("-b", diff);
        Assert.Contains("+B", diff);
    }

    [Theory]
    [InlineData("obj/Generated.g.cs", true)]
    [InlineData("src/Foo.designer.cs", true)]
    [InlineData("src/Foo.cs", false)]
    public void GeneratedDocumentsAreRecognized(string path, bool expected)
        => Assert.Equal(expected, GeneratedDocuments.IsGenerated(path));

    [Fact]
    public void AdviceNamesThePhase()
    {
        var notOpened = new WorkspaceStatus(WorkspacePhase.NotOpened, null, 0, 0, null);
        Assert.Contains("No solution is open", notOpened.Advice);
        var failed = new WorkspaceStatus(WorkspacePhase.Failed, "x.sln", 0, 0, "boom");
        Assert.Contains("failed to open", failed.Advice);
    }
}