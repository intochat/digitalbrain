using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

public sealed class FacadeTests
{
    [GenerateSerializer]
    [Alias("db.test.silent-request")]
    public sealed record SilentRequest(string Text) : Signal<SilentResponse>;

    [GenerateSerializer]
    [Alias("db.test.silent-response")]
    public sealed record SilentResponse(string Text) : Signal;

    [Fact]
    public async Task FacadeAndNeuronReferenceQueriesUseImplicitSubjects()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var cancellationToken = TestContext.Current.CancellationToken;
        await brain.Brain.ActivateAsync(cancellationToken);

        var announcerId = new NeuronId("announcer", new OwnerId(DigitalBrainNames.DefaultOwner), "facade");
        var announcer = brain.Grains.GetGrain<IAnnouncer>(announcerId.ToGrainId());
        var announcerReference = brain.Brain.Get<IAnnouncer>("facade");

        await announcer.Announce("hello");

        var rootJournal = await brain.Brain.ReadJournalAsync(
            JournalKind.Outgoing,
            cancellationToken: cancellationToken);
        var announcerJournal = await announcerReference.ReadJournalAsync(
            JournalKind.Outgoing,
            cancellationToken: cancellationToken);
        var synapses = await announcerReference.GetSynapsesAsync(cancellationToken);

        Assert.Contains(rootJournal.Delta, delivery => delivery.Signal is DigitalBrainActivated);
        Assert.Equal(2, announcerJournal.Delta.Count);
        Assert.Equal(2, synapses.Count);
        Assert.All(synapses, synapse => Assert.Equal(announcerId, synapse.Source));
        Assert.Empty(await brain.Brain.GetSynapsesAsync(cancellationToken));
    }

    [Fact]
    public async Task TypedRequest_UnhandledTargetFailsBeforeWaiting()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var silent = brain.Brain.Get<IPingSilent>("typed-request");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => silent.SendAsync<SilentResponse>(new SilentRequest("ignored"), cancellation.Token));

        Assert.False(cancellation.IsCancellationRequested);
        Assert.Contains(nameof(SilentRequest), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnerRoot_RefusesForeignQuerySubjects()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var owner = new OwnerId(DigitalBrainNames.DefaultOwner);
        var root = brain.Grains.GetGrain<IBrainNeuron>(IBrainNeuron.ForOwner(owner).ToGrainId());
        var foreign = new NeuronId("pingsink", new OwnerId("someone-else"), "private");

        await Assert.ThrowsAsync<NeuronAuthorizationException>(
            () => root.ReadNeuronSynapses(foreign));
    }
}
