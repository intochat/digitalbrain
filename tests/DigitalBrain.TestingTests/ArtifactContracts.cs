using System.Text;
using System.Text.Json;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class ArtifactContracts(TestingFixture fixture)
{
    private static readonly DateTimeOffset FixedEpoch =
        new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CleanupFailureAttachesMethodScopedEvidence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test =
            await fixture.CreateBrainAsync(cancellationToken);
        var alice = test.Owner("alice");
        var echo = alice.Neuron<IEchoNeuron>("artifact-fault");

        await test.Clock.AdvanceAsync(TimeSpan.FromHours(2), cancellationToken);
        _ = echo.FailNextJournalCommit("provider payload must stay out");

        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(
            async () => await test.DisposeAsync());
        var artifact = failure.Artifact;

        Assert.IsType<InvalidOperationException>(failure.InnerException);
        Assert.Equal(typeof(TestingFixture).FullName, artifact.FixtureId);
        Assert.Contains(TestingProbeModule.Id.Value, artifact.ModuleIds);
        Assert.Contains(alice.Id.Value, artifact.Owners);
        Assert.Equal(FixedEpoch, artifact.ClockOrigin);
        Assert.Equal(FixedEpoch + TimeSpan.FromHours(2), artifact.ClockUtc);
        Assert.Equal("fault-cleanup", artifact.CleanupStage);
        Assert.Contains(
            artifact.Events,
            item => item.Operation == "clock.advance"
                && item.State == "succeeded");
        Assert.Contains(
            artifact.Faults,
            item => item.Target == echo.Id.ToString()
                && item.State == "cleanup-leak");
        Assert.DoesNotContain(
            "provider payload must stay out",
            artifact.ToJson(),
            StringComparison.Ordinal);
        Assert.Contains(
            "digitalbrain-test.json",
            TestContext.Current.Attachments!.Keys);
    }

    [Fact]
    public async Task FrameworkFailurePreservesTheCauseAndAttachesEvidence()
    {
        await using var test =
            await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(
            () => test.Clock.AdvanceAsync(
                TimeSpan.Zero,
                cancellation.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(
            failure.InnerException);
        Assert.Contains(
            failure.Artifact.Events,
            item => item.Operation == "clock.advance"
                && item.State == "failed");
        Assert.Contains(
            "digitalbrain-test.json",
            TestContext.Current.Attachments!.Keys);
    }

    [Fact]
    public async Task SensitiveOwnerIdentifiersAreRedactedBeforeRingEviction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test =
            await fixture.CreateBrainAsync(cancellationToken);
        const string label = "client-token";
        var sensitive = test.Owner(label);
        var echo = sensitive.Neuron<IEchoNeuron>("sensitive-target");
        _ = echo.FailNextJournalCommit("provider payload");

        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(
            async () => await test.DisposeAsync());
        var artifact = failure.Artifact;
        var ownerId = sensitive.Id.Value;
        var target = echo.Id.ToString();
        string[] forbidden = [label, ownerId, target];

        Assert.All(
            artifact.Events,
            item =>
            {
                Assert.All(
                    forbidden,
                    raw =>
                    {
                        Assert.DoesNotContain(
                            raw,
                            item.Operation,
                            StringComparison.Ordinal);
                        Assert.DoesNotContain(
                            raw,
                            item.State,
                            StringComparison.Ordinal);
                        Assert.All(
                            item.Metadata,
                            field =>
                            {
                                Assert.DoesNotContain(
                                    raw,
                                    field.Key,
                                    StringComparison.Ordinal);
                                Assert.DoesNotContain(
                                    raw,
                                    field.Value,
                                    StringComparison.Ordinal);
                            });
                    });
            });
        Assert.All(
            artifact.Faults,
            fault =>
            {
                Assert.All(
                    forbidden,
                    raw => Assert.DoesNotContain(
                        raw,
                        fault.Target,
                        StringComparison.Ordinal));
            });
        Assert.All(
            forbidden,
            raw => Assert.DoesNotContain(
                raw,
                artifact.ToJson(),
                StringComparison.Ordinal));
        Assert.Contains(
            "[REDACTED]",
            artifact.ToJson(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArtifactIsRedactedAndBounded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test =
            await fixture.CreateBrainAsync(cancellationToken);
        var sensitive = test.Owner("client-token");
        _ = test.Owner(new string('x', 4096));

        for (var index = 0; index < 40; index++)
        {
            var owner = test.Owner($"owner-{index}");
            var neuron = owner.Neuron<IEchoNeuron>($"fault-{index}");
            _ = neuron.FailNextJournalCommit($"unrecorded-{index}");
        }

        for (var index = 0; index < 520; index++)
        {
            await test.Clock.AdvanceAsync(
                TimeSpan.Zero,
                cancellationToken);
        }

        var failure = await Assert.ThrowsAsync<BrainTestFailureException>(
            async () => await test.DisposeAsync());
        var artifact = failure.Artifact;
        var json = artifact.ToJson();

        Assert.True(artifact.Owners.Count <= 32);
        Assert.True(artifact.Events.Count <= 512);
        Assert.True(artifact.Faults.Count <= 32);
        Assert.DoesNotContain(
            sensitive.Id.Value,
            json,
            StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", json, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(json) <= 1024 * 1024);

        using var document = JsonDocument.Parse(json);
        AssertStringsAreBounded(document.RootElement);
    }

    [Fact]
    public async Task ValidationAndDisposedControlFailuresAreDiagnostic()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test =
            await fixture.CreateBrainAsync(cancellationToken);
        var echo = test.Neuron<IEchoNeuron>("disposed-controls");

        var cursorFailure = await Assert.ThrowsAsync<BrainTestFailureException>(
            () => echo.Outgoing.ReadAsync<Echoed>(
                -1,
                cancellationToken));
        Assert.IsType<ArgumentOutOfRangeException>(
            cursorFailure.InnerException);

        await test.DisposeAsync();

        var faultFailure = Assert.Throws<BrainTestFailureException>(
            () => echo.FailNextJournalCommit("not armed"));
        Assert.IsType<ObjectDisposedException>(
            faultFailure.InnerException);

        var restartFailure = await Assert.ThrowsAsync<BrainTestFailureException>(
            () => echo.RestartHostAsync(cancellationToken));
        Assert.IsType<ObjectDisposedException>(
            restartFailure.InnerException);
    }

    private static void AssertStringsAreBounded(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Assert.True(property.Name.Length <= 2048);
                    AssertStringsAreBounded(property.Value);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AssertStringsAreBounded(item);
                }

                break;

            case JsonValueKind.String:
                Assert.True(element.GetString()!.Length <= 2048);
                break;
        }
    }
}
