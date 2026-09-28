using DigitalBrain.Testing.Unit;
using IntoChat.Operations;

namespace IntoChat.Tests.Operations;

public sealed class ProblemReportFacts
{
    [Fact]
    public async Task AReportKeepsTheIntentIdAndSurvivesReactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        var reports = brain.Get<IProblemReports>("workspace-scope-1");

        var report = await reports.Add("workspace-1", "run-42", "The table did not open.");
        await brain.DeactivateAsync(reports, ct);

        Assert.Equal("run-42", report.IntentId);
        Assert.Equal(report, Assert.Single(await reports.Read()));
    }

    [Fact]
    public async Task AnEmptyIntentIdIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            brain.Get<IProblemReports>("workspace-scope-1").Add("workspace-1", "  ", "something broke"));
    }
}
