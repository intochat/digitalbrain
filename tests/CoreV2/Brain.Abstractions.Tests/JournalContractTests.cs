using Brain.Abstractions.Graph;
using Brain.Abstractions.Journal;
using Brain.Abstractions.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Xunit;

namespace Brain.Abstractions.Tests;

public sealed class JournalContractTests
{
    [Fact]
    public void Journal_records_require_causal_identity_and_positive_sequence()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Record(sequence: 0));
        Assert.Throws<ArgumentException>(() => Record(recordId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Record(activityId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Record(firingId: Guid.Empty));
    }

    [Fact]
    public void Journal_pages_are_monotonic_workspace_scoped_and_orleans_serializable()
    {
        var first = Record(sequence: 4);
        var second = Record(sequence: 5, recordId: Guid.NewGuid(), activityId: first.ActivityId);
        var page = new BrainJournalPage("workspace-a", first.ActivityId, 3, 5, [first, second], false);

        var roundTrip = RoundTrip(page);

        Assert.Equal("workspace-a", roundTrip.WorkspaceId);
        Assert.Equal([4L, 5L], roundTrip.Records.Select(record => record.Sequence));
        Assert.Throws<ArgumentException>(() =>
            new BrainJournalPage("workspace-b", first.ActivityId, 3, 5, [first], false));
        Assert.Throws<ArgumentException>(() =>
            new BrainJournalPage("workspace-a", first.ActivityId, 3, 4, [second, first], false));
    }

    [Fact]
    public void Brain_snapshots_preserve_live_synapses_and_usage_without_credentials()
    {
        var activity = Guid.NewGuid();
        var snapshot = new BrainSnapshot(
            "workspace-a",
            7,
            DateTimeOffset.Parse("2026-08-14T20:00:00Z"),
            [new BrainNeuronView("proof/source/workspace", "proof", "source", "workspace", 3)],
            [new BrainSynapseView(
                Guid.NewGuid(),
                2,
                "proof/source/workspace",
                "proof/assessment/workspace",
                "ProofProduced@1",
                "ProofProduced@1",
                "live",
                1,
                activity)]);

        var roundTrip = RoundTrip(snapshot);

        Assert.Equal(7, roundTrip.Sequence);
        Assert.Equal(1, Assert.Single(roundTrip.Synapses).UsageCount);
        var names = typeof(BrainJournalRecord)
            .GetProperties()
            .Concat(typeof(BrainSnapshot).GetProperties())
            .Select(property => property.Name);
        Assert.DoesNotContain(names, name =>
            name.Contains("token", StringComparison.OrdinalIgnoreCase)
            || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || name.Contains("credential", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Runtime_and_activity_grain_contracts_use_product_safe_serializable_messages()
    {
        Assert.Contains(typeof(IGrainWithStringKey), typeof(IBrainRuntimeGrain).GetInterfaces());
        Assert.Contains(typeof(IGrainWithStringKey), typeof(IBrainActivityGrain).GetInterfaces());

        var invocation = new BrainOperationInvocation(
            "Proof.Run@1",
            "{\"value\":\"journal-live\"}",
            "workspace-a",
            "principal-a",
            "acceptance-1");
        var roundTrip = RoundTrip(invocation);

        Assert.Equal("Proof.Run@1", roundTrip.OperationId);
        Assert.DoesNotContain(
            typeof(BrainOperationInvocation).GetProperties(),
            property => property.Name.Contains("credential", StringComparison.OrdinalIgnoreCase));
    }

    private static T RoundTrip<T>(T value)
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder => builder.AddAssembly(typeof(BrainJournalRecord).Assembly));
        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<Serializer<T>>();
        return serializer.Deserialize(serializer.SerializeToArray(value));
    }

    private static BrainJournalRecord Record(
        long sequence = 1,
        Guid? recordId = null,
        Guid? activityId = null,
        Guid? firingId = null)
        => new(
            sequence,
            recordId ?? Guid.NewGuid(),
            "workspace-a",
            activityId ?? Guid.NewGuid(),
            "principal-a",
            "proof/source/workspace",
            BrainJournalDirection.Outbound,
            "ProofProduced@1",
            firingId ?? Guid.NewGuid(),
            null,
            null,
            null,
            DateTimeOffset.Parse("2026-08-14T20:00:00Z"),
            1,
            "emitted",
            "Proof produced");
}
