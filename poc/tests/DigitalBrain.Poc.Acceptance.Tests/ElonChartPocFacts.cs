using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Creator;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;
using DigitalBrain.Poc.Social.Contracts;
using Xunit;

namespace DigitalBrain.Poc.Acceptance.Tests;

public sealed class ElonChartPocFacts
{
    [Fact]
    public async Task ApprovedModuleTurnsOnlyElonPostsIntoChartPointsAcrossRestart()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        await using var fixture = ElonChartPocFixture.Create(root);
        var owner = fixture.Owners.PrincipalForTest("owner-a");
        var session = fixture.Owners.SessionFor("owner-a");
        var firstObservedAt = DateTimeOffset.Parse("2026-08-09T10:00:00Z");
        var candidate = await fixture.CreateApprovedAsync(
            owner,
            "elon-chart",
            "elonmusk",
            TestContext.Current.CancellationToken);
        var intent = candidate.Intent;

        Assert.True(await new FileCandidateFamilyRegistry(root).IsReservedForAsync(
            owner,
            intent.Family,
            TestContext.Current.CancellationToken));

        var first = await fixture.PromoteAsync(
            owner,
            candidate.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        await first.FireTrustedAsync(
            session,
            new SocialPostObserved("post-1", "elonmusk", firstObservedAt),
            TestContext.Current.CancellationToken);
        await first.FireTrustedAsync(
            session,
            new SocialPostObserved("post-2", "other", firstObservedAt.AddMinutes(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal([1], (await first.ChartAsync(
            session,
            "elon-chart",
            TestContext.Current.CancellationToken)).Points.Select(point => point.Ordinal));
        Assert.Equal(
            ["SocialPostObserved", "ElonPostMatched", "AddChartPoint", "ChartPointAdded"],
            await first.JournalKindsForInputAsync(
                session,
                "post-1",
                TestContext.Current.CancellationToken));
        Assert.Equal(
            ["SocialPostObserved"],
            await first.JournalKindsForInputAsync(
                session,
                "post-2",
                TestContext.Current.CancellationToken));

        var second = await fixture.RestartAsync(
            owner,
            intent.Family,
            TestContext.Current.CancellationToken);
        Assert.NotEqual(first.ProcessId, second.ProcessId);
        await second.FireTrustedAsync(
            session,
            new SocialPostObserved("post-3", "elonmusk", firstObservedAt.AddMinutes(2)),
            TestContext.Current.CancellationToken);
        await second.FireTrustedAsync(
            session,
            new SocialPostObserved("post-1", "elonmusk", firstObservedAt),
            TestContext.Current.CancellationToken);

        Assert.Equal([1, 2], (await second.ChartAsync(
            session,
            "elon-chart",
            TestContext.Current.CancellationToken)).Points.Select(point => point.Ordinal));
        Assert.Equal(
            2,
            await second.GeneratedAcceptedCountAsync(
                session,
                intent.Family,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            ["SocialPostObserved", "ElonPostMatched", "AddChartPoint", "ChartPointAdded"],
            await second.JournalKindsForInputAsync(
                session,
                "post-3",
                TestContext.Current.CancellationToken));
        Assert.Equal(
            [
                "SocialPostObserved", "ElonPostMatched", "AddChartPoint", "ChartPointAdded",
                "SocialPostObserved",
                "SocialPostObserved", "ElonPostMatched", "AddChartPoint", "ChartPointAdded",
            ],
            await second.OrderedLogicalJournalKindsAsync(
                session,
                TestContext.Current.CancellationToken));

        Console.WriteLine($"Task 8 route PID evidence: first={first.ProcessId}, restart={second.ProcessId}");
    }

    [Fact]
    public async Task TwoOwnerFamiliesWithTheSameGeneratedTypeNamesRouteIndependentlyOverOpaqueHttpSessions()
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        await using var fixture = ElonChartPocFixture.Create(root);
        var ownerA = fixture.Owners.PrincipalForTest("owner-a");
        var ownerB = fixture.Owners.PrincipalForTest("owner-b");
        var sessionA = fixture.Owners.SessionFor("owner-a");
        var sessionB = fixture.Owners.SessionFor("owner-b");
        var intentA = ElonChartAuthoringIntent.DefaultTrustedFixture;
        var intentB = ElonChartAuthoringIntent.ForTrustedFixture(
            CandidateFamilyId.Parse("cf_bbbbbbbbbbbbbbbbbbbbbbbbbb"),
            "owner-b-chart",
            "elonmusk");
        var candidateA = await fixture.CreateApprovedTrustedFixtureAsync(
            ownerA,
            intentA,
            TestContext.Current.CancellationToken);
        var candidateB = await fixture.CreateApprovedTrustedFixtureAsync(
            ownerB,
            intentB,
            TestContext.Current.CancellationToken);

        _ = await fixture.PromoteAsync(
            ownerA,
            candidateA.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        var host = await fixture.PromoteAsync(
            ownerB,
            candidateB.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        var activeA = await fixture.Supervisor.CurrentAsync(
            ownerA,
            intentA.Family,
            TestContext.Current.CancellationToken);
        await activeA.FireTrustedAsync(
            sessionA,
            new SocialPostObserved("owner-a-post", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken);
        await host.FireTrustedAsync(
            sessionB,
            new SocialPostObserved("owner-b-post", "elonmusk", DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken);

        Assert.Equal(host.ProcessId, activeA.ProcessId);
        Assert.NotEqual(
            $"db.poc.family.{intentA.Family.Value}.matched.v1",
            $"db.poc.family.{intentB.Family.Value}.matched.v1");
        Assert.Equal([1], (await activeA.ChartAsync(
            sessionA,
            intentA.ChartId,
            TestContext.Current.CancellationToken)).Points.Select(point => point.Ordinal));
        Assert.Equal([1], (await host.ChartAsync(
            sessionB,
            intentB.ChartId,
            TestContext.Current.CancellationToken)).Points.Select(point => point.Ordinal));

        using var client = new HttpClient { BaseAddress = host.ProjectionBaseUri };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(
            $"poc/charts/{intentA.ChartId}",
            TestContext.Current.CancellationToken)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(
            $"poc/charts/{intentA.ChartId}",
            TestContext.Current.CancellationToken)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionA.Token);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(
            $"poc/charts/{intentB.ChartId}",
            TestContext.Current.CancellationToken)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionB.Token);
        var projection = await client.GetFromJsonAsync<ChartProjectionWire>(
            $"poc/charts/{intentB.ChartId}",
            TestContext.Current.CancellationToken);
        Assert.Equal([1], projection!.Points.Select(point => point.Ordinal));
    }

    private sealed record ChartProjectionWire(string ChartId, ChartPointWire[] Points);

    private sealed record ChartPointWire(string SourcePostId, DateTimeOffset OccurredAt, int Ordinal);
}
