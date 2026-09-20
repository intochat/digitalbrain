using DigitalBrain.Coding;
using Xunit;

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
    public async Task BuildParsesCompilerErrors()
    {
        const string output = "C:\\repo\\src\\A\\Thing.cs(12,9): error CS0103: The name 'x' does not exist [C:\\repo\\src\\A\\A.csproj]";
        var runner = new StubProcessRunner(new ProcessResult(1, output, string.Empty, TimeSpan.FromSeconds(1), TimedOut: false));
        var outcome = await new DotnetRunner(runner).BuildAsync(Path.Combine(Path.GetTempPath(), "code-tests", "Sample.sln"), null, CancellationToken.None);
        Assert.False(outcome.Succeeded);
        var error = Assert.Single(outcome.Errors);
        Assert.Equal("CS0103", error.Id);
        Assert.Equal(12, error.Line);
    }

    [Fact]
    public async Task TestParsesTheSummaryAndFailures()
    {
        const string output = "total: 3, failed: 1, succeeded: 2, skipped: 0\nfailed Sample.Case (12ms)\n  Expected 1, got 2.\n";
        var runner = new StubProcessRunner(new ProcessResult(1, output, string.Empty, TimeSpan.FromSeconds(1), TimedOut: false));
        var outcome = await new DotnetRunner(runner).TestAsync(Path.Combine(Path.GetTempPath(), "code-tests", "Sample.sln"), null, null, CancellationToken.None);
        Assert.False(outcome.Succeeded);
        Assert.Equal(3, outcome.Total);
        Assert.Equal(1, outcome.Failed);
        Assert.Equal("Sample.Case", Assert.Single(outcome.Failures).Name);
    }

    [Fact]
    public void AdviceNamesThePhase()
    {
        var notOpened = new WorkspaceStatus(WorkspacePhase.NotOpened, null, 0, 0, null);
        Assert.Contains("No solution is open", notOpened.Advice);
        var failed = new WorkspaceStatus(WorkspacePhase.Failed, "x.sln", 0, 0, "boom");
        Assert.Contains("failed to open", failed.Advice);
    }

    private sealed class StubProcessRunner(ProcessResult result) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult(result);
    }
}