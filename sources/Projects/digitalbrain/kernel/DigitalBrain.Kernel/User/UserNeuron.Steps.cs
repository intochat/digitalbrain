using DigitalBrain.Runtime.User;
using DigitalBrain.Kernel.Conversation;
using DigitalBrain.Kernel.User;

namespace DigitalBrain.Kernel.Tests.User;

// Plain xUnit fast tests for UserNeuron using the testable-implementation pattern
// (same approach as ConversationGrainTests). All Orleans infrastructure is
// replaced by in-process stubs so no silo or Aspire cluster is needed.
//
// TestableUserNeuron mirrors UserNeuron's logic against a stub IConversation
// so the core behavior (message persistence + synapse construction) is verified
// without the DurableGrain/service-provider dependency.

public sealed class UserNeuronTests
{
    static readonly DateTimeOffset T0 = new(2026, 5, 12, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SubmitPromptAsync_persistsUserMessageAndFiresSynapse()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new ManualClock(T0);
        var conv = new SpyConversation();
        var neuron = new TestableUserNeuron("default", conv, clock);
        var correlationId = Guid.NewGuid();

        await neuron.SubmitPromptAsync("hello", correlationId, ct);

        // Conversation must contain exactly one user message.
        Assert.Single(conv.Messages);
        Assert.Equal(ChatRole.User, conv.Messages[0].Role);
        Assert.Equal("hello", conv.Messages[0].Text);
        Assert.Equal(correlationId, conv.Messages[0].CorrelationId);

        // Synapse must be fired with correct fields.
        var synapse = Assert.Single(neuron.FiredSynapses);
        Assert.Equal(correlationId, synapse.CorrelationId);
        Assert.Equal(nameof(UserNeuron), synapse.CallerNeuronType);
        Assert.Equal("default", synapse.UserId);
        Assert.Equal("hello", synapse.Text);
        Assert.Equal(StreamKeys.StringKeyToGuid("default"), synapse.CallerNeuronId);
    }

    [Fact]
    public async Task GetRecentCorrelationIdsAsync_filtersByTimeWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new ManualClock(T0);
        var conv = new SpyConversation();
        var neuron = new TestableUserNeuron("default", conv, clock);

        // Seed messages at -2 h, -25 h, -5 d relative to T0.
        var recent = Guid.NewGuid();
        var old1 = Guid.NewGuid();
        var old2 = Guid.NewGuid();

        conv.Messages.Add(new ChatMessage(Guid.NewGuid(), ChatRole.User, "a", null, recent, T0.AddHours(-2)));
        conv.Messages.Add(new ChatMessage(Guid.NewGuid(), ChatRole.User, "b", null, old1, T0.AddHours(-25)));
        conv.Messages.Add(new ChatMessage(Guid.NewGuid(), ChatRole.User, "c", null, old2, T0.AddDays(-5)));

        var ids = await neuron.GetRecentCorrelationIdsAsync(TimeSpan.FromHours(24), ct);

        Assert.Single(ids);
        Assert.Equal(recent, ids[0]);
    }

    // Controllable clock — avoids FakeTimeProvider package dependency.
    sealed class ManualClock
    {
        public DateTimeOffset UtcNow { get; set; }
        public ManualClock(DateTimeOffset start) => UtcNow = start;
    }

    // Implements IConversation against a plain List<ChatMessage> seeded by tests.
    sealed class SpyConversation : IConversation
    {
        public List<ChatMessage> Messages { get; } = [];

        public Task AppendUserMessageAsync(Guid id, string text, Guid correlationId, CancellationToken ct)
        {
            // Use DateTimeOffset.MinValue as placeholder timestamp; the neuron sets it via TimeProvider.
            Messages.Add(new ChatMessage(id, ChatRole.User, text, null, correlationId, DateTimeOffset.MinValue));
            return Task.CompletedTask;
        }

        public Task AppendAssistantMessageAsync(Guid id, string? text, string? rfwEnvelopeJson, Guid correlationId, CancellationToken ct)
        {
            Messages.Add(new ChatMessage(id, ChatRole.Assistant, text ?? string.Empty, rfwEnvelopeJson, correlationId, DateTimeOffset.MinValue));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ChatMessage>> RecentAsync(int count, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.TakeLast(count).ToList());

        public Task<IReadOnlyList<ChatMessage>> SinceAsync(DateTimeOffset since, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(m => m.Timestamp >= since).ToList());

        public Task<IReadOnlyList<ChatMessage>> SearchAsync(string query, DateTimeOffset? since, DateTimeOffset? until, int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Take(limit).ToList());
    }

    // Implements IUserNeuron directly (mirrors UserNeuron's logic) so the DurableGrain
    // constructor never runs. This is the same testable-implementation approach used by
    // ConversationGrainTests.TestableConversation.
    sealed class TestableUserNeuron(string userId, IConversation conversation, ManualClock clock) : IUserNeuron
    {
        public List<UserPromptReceived> FiredSynapses { get; } = [];

        public async Task SubmitPromptAsync(string text, Guid correlationId, CancellationToken ct)
        {
            await conversation.AppendUserMessageAsync(Guid.NewGuid(), text, correlationId, ct);

            FiredSynapses.Add(new UserPromptReceived(UserId:             userId,
        Text:               text) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: correlationId,
            causationId: null,
            callerNeuronId: StreamKeys.StringKeyToGuid(userId),
            callerNeuronType: nameof(UserNeuron),
            receiverNeuronId: Guid.NewGuid(),
            receiverNeuronType: "IntentNeuron",
            timestamp: clock.UtcNow
        ) });
        }

        public async Task<IReadOnlyList<Guid>> GetRecentCorrelationIdsAsync(TimeSpan since, CancellationToken ct)
        {
            var cutoff = clock.UtcNow - since;
            var messages = await conversation.SinceAsync(cutoff, ct);
            return messages
                .Where(m => m.Role == ChatRole.User)
                .Select(m => m.CorrelationId)
                .Distinct()
                .ToArray();
        }

        // Unused interface members required by INeuronWithStringKey.
        public Task<IReadOnlyList<DigitalBrain.Core.Neurons.Synapse>> GetIncomingJournalAsync(int fromIndex = 0, int toIndex = int.MaxValue) => Task.FromResult<IReadOnlyList<DigitalBrain.Core.Neurons.Synapse>>([]);
        public Task<IReadOnlyList<DigitalBrain.Core.Neurons.Synapse>> GetOutgoingJournalAsync(int fromIndex = 0, int toIndex = int.MaxValue) => Task.FromResult<IReadOnlyList<DigitalBrain.Core.Neurons.Synapse>>([]);
        public Task<int> GetIncomingCountAsync() => Task.FromResult(0);
        public Task<int> GetOutgoingCountAsync() => Task.FromResult(0);
    }
}
