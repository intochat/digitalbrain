using DigitalBrain.Scripting.Startup;
using Xunit;

namespace DigitalBrain.Scripting.Tests;

public sealed class CSharpStartupScriptRunnerTests
{
    private readonly CSharpStartupScriptRunner runner = new();

    [Fact]
    public async Task Script_can_read_the_connected_brain_owner()
    {
        var script = StartupScript.FromSource("start.cs", "return Brain.Owner.Value;");

        var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice", result.Summary);
    }

    [Fact]
    public async Task Compilation_errors_are_returned_as_diagnostics()
    {
        var script = StartupScript.FromSource("start.cs", "this is not C#;");

        var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task Runtime_errors_are_returned_without_terminating_the_worker()
    {
        var script = StartupScript.FromSource("start.cs", "throw new InvalidOperationException(\"boom\");");

        var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("boom", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Supplied_cancellation_is_propagated()
    {
        var script = StartupScript.FromSource("start.cs", "return 1;");
        var cancellationToken = new CancellationToken(canceled: true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.RunAsync(
            script,
            new FakeDigitalBrain("alice"),
            cancellationToken));
    }
}
