using DigitalBrain.Testing;

using DigitalBrain.Core.Tests.Support;

namespace DigitalBrain.Core.Tests.Physics;

public sealed class NestedAsksTests(BrainTestClusters clusters) : DigitalBrainTest(clusters)
{
    protected override void Compose(DigitalBrainTestBuilder brain)
        => brain.AddModule<RecallChat>().AddModule<EpisodicMemory>().AddModule<AssistantLedger>();

    [Fact(DisplayName = "Nested Ask opens a pin, stamps Answers on the MemoryHit continuation, and journals AssistantSaid on both neurons without Answer<>")]
    public async Task NestedAskPinsAnswersAndContinues()
    {
        var ct = Cancellation;
        var context = "desk";
        var session = Brain.Session(context);
        var chatId = new NeuronId("recallchat", context);
        var memoryId = new NeuronId("episodicmemory", context);
        var question = "Berlin office";

        await session.EmitAsync(new UserAsked(question), ct);

        var chatDone = await WaitForJournalAsync(
            chatId,
            reading => reading.AllSaid<AssistantSaid>().Count == 1,
            "said AssistantSaid after MemoryHit continuation",
            ct);

        var sessionReading = await ReadAsync(session.Id, ct);
        var userSaid = sessionReading.SaidSingle<UserAsked>();
        Assert.Equal("declared", userSaid.DeliveryTo(chatId).Via);
        Assert.Equal(question, Assert.IsType<UserAsked>(userSaid.Body).Text);

        var userHeard = chatDone.HeardSingle<UserAsked>();
        Assert.Equal(session.Id, userHeard.Metadata.Source);
        Assert.Equal(userSaid.Position, userHeard.Metadata.Sequence);

        var askSaid = chatDone.SaidSingle<MemoryQuery>();
        Assert.Equal("ask", askSaid.DeliveryTo(memoryId).Via);
        Assert.Equal(new SynapseRef(session.Id, userSaid.Position), askSaid.Cause);
        Assert.Equal(question, Assert.IsType<MemoryQuery>(askSaid.Body).Query);
        var askRef = new SynapseRef(chatId, askSaid.Position);

        var memoryReading = await ReadAsync(memoryId, ct);
        var queryHeard = memoryReading.HeardSingle<MemoryQuery>();
        Assert.Equal(chatId, queryHeard.Metadata.Source);
        Assert.Equal(askSaid.Position, queryHeard.Metadata.Sequence);
        Assert.Equal(question, Assert.IsType<MemoryQuery>(queryHeard.Body).Query);

        var hitSaid = memoryReading.SaidSingle<MemoryHit>();
        Assert.Equal(askRef, hitSaid.Answers);
        Assert.NotNull(hitSaid.DeliveryToOrNull(chatId));
        Assert.Equal($"recall:{question}", Assert.IsType<MemoryHit>(hitSaid.Body).Snippet);

        var hitHeard = chatDone.HeardSingle<MemoryHit>();
        Assert.Equal(memoryId, hitHeard.Metadata.Source);
        Assert.Equal(hitSaid.Position, hitHeard.Metadata.Sequence);
        Assert.Equal(askRef, hitHeard.Answers);
        Assert.Equal($"recall:{question}", Assert.IsType<MemoryHit>(hitHeard.Body).Snippet);

        var assistantSaid = chatDone.SaidSingle<AssistantSaid>();
        Assert.Equal(new SynapseRef(memoryId, hitHeard.Metadata.Sequence), assistantSaid.Cause);
        Assert.Equal($"recall:{question}", Assert.IsType<AssistantSaid>(assistantSaid.Body).Text);

        Assert.Empty(memoryReading.AllSaid<AssistantSaid>());
        Assert.Empty(chatDone.AllSaid<MemoryHit>());
    }
}
