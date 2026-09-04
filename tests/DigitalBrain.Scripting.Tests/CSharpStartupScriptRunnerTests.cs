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
    public async Task Script_can_see_chart_and_x_account_contracts()
    {
        var script = StartupScript.FromSource(
            "elon-chart.cs",
            "return typeof(IChart).Name + typeof(IXAccount).Name + typeof(NewPost).Name;");

        var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal("IChartIXAccountNewPost", result.Summary);
    }

    [Fact]
    public async Task Elon_dashboard_script_compiles()
    {
        var script = StartupScript.FromSource(
            "elon-chart",
            """
            var chart = Brain.GetEntity<IChart>("elon-activity");
            await chart.Render(new ChartState("Elon on X", "line", Array.Empty<ChartPoint>()));
            await Brain.GetEntity<ISurface>("desk").Open(
                new SurfaceScene("chart:elon-activity", "Elon on X"),
                8);
            await foreach (var page in Brain.Get<IXAccount>("elon").WatchJournalAsync(
                JournalKind.Outgoing,
                0,
                CancellationToken))
            {
                foreach (var delivery in page.Delta)
                {
                    if (delivery.Signal is NewPost post)
                    {
                        await chart.Append(
                            new ChartPoint(post.Text, 1, EventId: delivery.SignalId.ToString()),
                            "Elon on X");
                    }
                }
            }
            """);

        var result = await runner.RunAsync(script, new FakeDigitalBrain("alice"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not supported", result.Summary, StringComparison.OrdinalIgnoreCase);
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
