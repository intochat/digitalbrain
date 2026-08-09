using DigitalBrain.Poc.Creator;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;
using DigitalBrain.Poc.Social.Contracts;
using Xunit;

namespace DigitalBrain.Poc.Acceptance.Tests;

public sealed class CrashRecoveryFacts
{
    [Fact]
    public async Task NonMatchingAuthorDoesNotTriggerTheGeneratedLocalCommitFault()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        await using var fixture = ElonChartPocFixture.Create(root);
        var owner = fixture.Owners.PrincipalForTest("owner-a");
        var session = fixture.Owners.SessionFor("owner-a");
        var intent = ElonChartAuthoringIntent.DefaultTrustedFixture;
        var candidate = await fixture.CreateApprovedTrustedFixtureAsync(
            owner,
            intent,
            TestContext.Current.CancellationToken);
        var host = await fixture.PromoteAsync(
            owner,
            candidate.Id,
            HostFault.AfterGeneratedLocalOutboxCommitBeforeForwarderAcknowledgement,
            TestContext.Current.CancellationToken);

        await host.FireTrustedAsync(
            session,
            new SocialPostObserved("other-author", "not-elon", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [],
            (await host.ChartAsync(
                session,
                intent.ChartId,
                TestContext.Current.CancellationToken)).Points);
    }

    [Fact]
    public async Task RestartReplaysChartDeliveryCommittedBeforeUpstreamAcknowledgementExactlyOnce()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        await using var fixture = ElonChartPocFixture.Create(root);
        var owner = fixture.Owners.PrincipalForTest("owner-a");
        var session = fixture.Owners.SessionFor("owner-a");
        var intent = ElonChartAuthoringIntent.DefaultTrustedFixture;
        var candidate = await fixture.CreateApprovedTrustedFixtureAsync(
            owner,
            intent,
            TestContext.Current.CancellationToken);
        var first = await fixture.PromoteAsync(
            owner,
            candidate.Id,
            HostFault.AfterChartNeuronCommitBeforeUpstreamOutboxAcknowledgement,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() => first.FireTrustedAsync(
            session,
            new SocialPostObserved("post-1", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken));
        await first.WaitForExitAsync(TestContext.Current.CancellationToken);

        var second = await fixture.RestartAsync(
            owner,
            intent.Family,
            TestContext.Current.CancellationToken);
        Assert.Equal([1], (await second.ChartAsync(
            session,
            intent.ChartId,
            TestContext.Current.CancellationToken)).Points.Select(point => point.Ordinal));
        await second.ReplayLastChartDeliveryAsync(session, TestContext.Current.CancellationToken);
        Assert.Equal([1], (await second.ChartAsync(
            session,
            intent.ChartId,
            TestContext.Current.CancellationToken)).Points.Select(point => point.Ordinal));
    }

    [Fact]
    public async Task RestartDeserializesAndDeliversCommittedGeneratedSynapse()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        await using var fixture = ElonChartPocFixture.Create(root);
        var owner = fixture.Owners.PrincipalForTest("owner-a");
        var session = fixture.Owners.SessionFor("owner-a");
        var intent = ElonChartAuthoringIntent.DefaultTrustedFixture;
        var candidate = await fixture.CreateApprovedTrustedFixtureAsync(
            owner,
            intent,
            TestContext.Current.CancellationToken);
        var first = await fixture.PromoteAsync(
            owner,
            candidate.Id,
            HostFault.AfterGeneratedLocalOutboxCommitBeforeForwarderAcknowledgement,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<Exception>(() => first.FireTrustedAsync(
            session,
            new SocialPostObserved("post-1", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken));
        await first.WaitForExitAsync(TestContext.Current.CancellationToken);

        var second = await fixture.RestartAsync(
            owner,
            intent.Family,
            TestContext.Current.CancellationToken);
        Assert.Equal([1], (await second.ChartAsync(
            session,
            intent.ChartId,
            TestContext.Current.CancellationToken)).Points.Select(point => point.Ordinal));
        Assert.Equal(
            ["SocialPostObserved", "ElonPostMatched", "AddChartPoint", "ChartPointAdded"],
            await second.JournalKindsForInputAsync(
                session,
                "post-1",
                TestContext.Current.CancellationToken));
    }
}
