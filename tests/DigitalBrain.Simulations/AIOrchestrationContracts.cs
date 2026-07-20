using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class AIOrchestrationContracts
{
    [Fact(DisplayName = "Concurrent asks typed models independently with the same immutable input")]
    public async Task ConcurrentModelsRemainIndependent()
    {
        using var llama = new OrchestrationChatClient("llama-independent");
        using var gpt = new OrchestrationChatClient("gpt-independent");
        var cluster = await StartClusterAsync(llama, gpt);

        try
        {
            var owner = new OwnerId("concurrent-models");
            var panelId = NeuronId.For<ITestConcurrent>(owner, "panel");
            var probeId = NeuronId.For<IAIOrchestrationProbe>(owner, "probe");
            var probe = cluster.Client.GetGrain<IAIOrchestrationProbe>(probeId.ToGrainId());
            ChatMessage[] request =
            [
                new(ChatRole.System, "shared-system"),
                new(ChatRole.User, "shared-question")
            ];

            var response = await probe.CallAsync(panelId, request);
            var panelIncoming = await probe.ReadJournalAsync(panelId, JournalKind.Incoming);
            var panelOutgoing = await probe.ReadJournalAsync(panelId, JournalKind.Outgoing);
            var llamaId = NeuronId.For<ILlama32>(owner, "panel");
            var gptId = NeuronId.For<IGpt56>(owner, "panel");
            var llamaIncoming = await probe.ReadJournalAsync(llamaId, JournalKind.Incoming);
            var gptIncoming = await probe.ReadJournalAsync(gptId, JournalKind.Incoming);

            var llamaCall = Assert.Single(llama.Calls);
            var gptCall = Assert.Single(gpt.Calls);
            var outerRequest = Assert.Single(
                panelIncoming.Delta,
                delivery => delivery.Synapse is CapabilityRequested);
            var childRequests = panelOutgoing.Delta
                .Where(delivery => delivery.Synapse is CapabilityRequested)
                .ToArray();

            Assert.Equal(["shared-system", "shared-question"], llamaCall.Select(message => message.Text));
            Assert.Equal(["shared-system", "shared-question"], gptCall.Select(message => message.Text));
            Assert.DoesNotContain(llamaCall, message => message.Text == "gpt-independent");
            Assert.DoesNotContain(gptCall, message => message.Text == "llama-independent");
            Assert.Contains("llama-independent", response.Text, StringComparison.Ordinal);
            Assert.Contains("gpt-independent", response.Text, StringComparison.Ordinal);
            Assert.Equal(2, childRequests.Length);
            AssertCapabilityRequest(
                Assert.Single(childRequests, delivery => ((CapabilityRequested)delivery.Synapse).Target == llamaId),
                outerRequest,
                panelId,
                llamaId,
                llamaIncoming);
            AssertCapabilityRequest(
                Assert.Single(childRequests, delivery => ((CapabilityRequested)delivery.Synapse).Target == gptId),
                outerRequest,
                panelId,
                gptId,
                gptIncoming);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "orchestrations reject participant contracts that are not typed ILLM or IAgent capabilities")]
    public async Task InvalidParticipantContractsAreRejected()
    {
        using var llama = new OrchestrationChatClient("unused-llama");
        using var gpt = new OrchestrationChatClient("unused-gpt");
        var cluster = await StartClusterAsync(llama, gpt);

        try
        {
            var owner = new OwnerId("invalid-participant");
            var target = NeuronId.For<IInvalidConcurrent>(owner, "invalid");
            var probe = cluster.Client.GetGrain<IAIOrchestrationProbe>(
                NeuronId.For<IAIOrchestrationProbe>(owner, "probe").ToGrainId());

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                probe.CallAsync(target, [new ChatMessage(ChatRole.User, "do not run")]));

            Assert.Contains(nameof(ILLM), failure.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(IAgent), failure.Message, StringComparison.Ordinal);
            Assert.Empty(llama.Calls);
            Assert.Empty(gpt.Calls);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "GroupChat resumes its one protected MAF session after deactivation")]
    public async Task GroupChatResumesProtectedSessionAfterDeactivation()
    {
        using var llama = new OrchestrationChatClient("llama-independent");
        using var gpt = GroupGptClient();
        var cluster = await StartClusterAsync(llama, gpt);

        try
        {
            var owner = new OwnerId("group-resume");
            var groupId = NeuronId.For<ITestGroupChat>(owner, "council");
            var probe = cluster.Client.GetGrain<IAIOrchestrationProbe>(
                NeuronId.For<IAIOrchestrationProbe>(owner, "probe").ToGrainId());

            var first = await probe.CallAsync(
                groupId,
                [new ChatMessage(ChatRole.User, "first-question")]);
            var protectedState = await probe.ReadGroupStateAsync(groupId);
            var firstActivation = await probe.GroupActivationAsync(groupId);
            var reconciliation = gpt.Calls[1];
            var envelopeText = System.Text.Encoding.UTF8.GetString(protectedState);

            Assert.Contains("gpt-reconciled", first.Text, StringComparison.Ordinal);
            Assert.Contains(reconciliation, message => message.Text == "llama-independent");
            Assert.Contains(reconciliation, message => message.Text == "gpt-independent");
            Assert.NotEmpty(protectedState);
            Assert.DoesNotContain("first-question", envelopeText, StringComparison.Ordinal);
            Assert.DoesNotContain("gpt-reconciled", envelopeText, StringComparison.Ordinal);
            Assert.DoesNotContain("llama-independent", envelopeText, StringComparison.Ordinal);
            Assert.DoesNotContain("gpt-independent", envelopeText, StringComparison.Ordinal);
            using (var envelope = System.Text.Json.JsonDocument.Parse(protectedState))
            {
                Assert.Equal(
                    ["Fingerprint", "FormatVersion", "MafVersion", "Participants", "ProtectedSession"],
                    envelope.RootElement.EnumerateObject()
                        .Select(property => property.Name)
                        .Order(StringComparer.Ordinal));
            }

            await probe.DeactivateGroupAsync(groupId);

            var second = await probe.CallAsync(
                groupId,
                [new ChatMessage(ChatRole.User, "second-question")]);
            var secondActivation = await probe.GroupActivationAsync(groupId);
            var resumedState = await probe.ReadGroupStateAsync(groupId);
            Assert.Equal(2, llama.Calls.Count);
            Assert.Equal(4, gpt.Calls.Count);
            var secondReconciliation = gpt.Calls[3];

            Assert.Contains("gpt-reconciled", second.Text, StringComparison.Ordinal);
            Assert.NotEqual(firstActivation, secondActivation);
            Assert.NotEqual(protectedState, resumedState);
            Assert.Contains(llama.Calls[1], message => message.Text == "first-question");
            Assert.Contains(llama.Calls[1], message => message.Text == "second-question");
            Assert.Contains(secondReconciliation, message => message.Text == "first-question");
            Assert.Contains(secondReconciliation, message => message.Text == "second-question");
            Assert.Contains(secondReconciliation, message => message.Text == "llama-independent");
            Assert.Contains(secondReconciliation, message => message.Text == "gpt-independent");
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "GroupChat rejects composition drift before session access or participant calls")]
    public async Task GroupChatRejectsCompositionDriftWithoutMutation()
    {
        using var llama = new OrchestrationChatClient("llama-independent");
        using var gpt = GroupGptClient();
        var cluster = await StartClusterAsync(llama, gpt);

        try
        {
            var owner = new OwnerId("group-drift");
            var groupId = NeuronId.For<ITestGroupChat>(owner, "council");
            var probe = cluster.Client.GetGrain<IAIOrchestrationProbe>(
                NeuronId.For<IAIOrchestrationProbe>(owner, "probe").ToGrainId());

            await probe.CallAsync(groupId, [new ChatMessage(ChatRole.User, "establish-session")]);
            var before = await probe.ReadGroupStateAsync(groupId);
            await probe.ChangeGroupParticipantAsync(groupId, "changed-participant");
            await probe.DeactivateGroupAsync(groupId);
            var carried = await probe.ReadGroupStateAsync(groupId);

            Assert.Equal(before, carried);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => probe.CallAsync(
                groupId,
                [new ChatMessage(ChatRole.User, "must-not-run")]));
            var after = await probe.ReadGroupStateAsync(groupId);

            Assert.Contains("migration", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("reset", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, after);
            Assert.Single(llama.Calls);
            Assert.Equal(2, gpt.Calls.Count);

            await probe.ChangeGroupParticipantAsync(groupId, name: null);
            await probe.DeactivateGroupAsync(groupId);

            var resumed = await probe.CallAsync(
                groupId,
                [new ChatMessage(ChatRole.User, "compatible-again")]);

            Assert.Contains("gpt-reconciled", resumed.Text, StringComparison.Ordinal);
            Assert.Equal(2, llama.Calls.Count);
            Assert.Equal(4, gpt.Calls.Count);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    private static async Task<InProcessTestCluster> StartClusterAsync(
        OrchestrationChatClient llama,
        OrchestrationChatClient gpt)
    {
        var builder = new InProcessTestClusterBuilder(1);

        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddDigitalBrain("ai-orchestration-contracts");
            AIModule.Configure(silo);
            silo.UseInMemoryReminderService();
            silo.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
            silo.Services.AddKeyedSingleton<IChatClient>(typeof(Llama32), llama);
            silo.Services.AddKeyedSingleton<IChatClient>(typeof(Gpt56), gpt);
        });
        builder.ConfigureClient(client =>
        {
            client.Services.AddSerializer(serializer => serializer.AddJsonSerializer(
                type => type == typeof(ChatMessage) || type == typeof(ChatResponse)));
        });

        var cluster = builder.Build();
        await cluster.DeployAsync();

        return cluster;
    }

    private static OrchestrationChatClient GroupGptClient()
        => new(messages => messages.Any(message => message.Text == "llama-independent")
            && messages.Any(message => message.Text == "gpt-independent")
                ? "gpt-reconciled"
                : "gpt-independent");

    private static void AssertCapabilityRequest(
        SynapseDelivery request,
        SynapseDelivery cause,
        NeuronId caller,
        NeuronId target,
        JournalRead targetIncoming)
    {
        var capability = Assert.IsType<CapabilityRequested>(request.Synapse);
        var received = Assert.Single(
            targetIncoming.Delta,
            delivery => delivery.SynapseId == request.SynapseId);

        Assert.Equal(caller, request.Caller);
        Assert.Equal(target, capability.Target);
        Assert.Equal(typeof(ILLM).FullName, capability.Contract);
        Assert.Equal(nameof(ILLM.RespondAsync), capability.Method);
        Assert.Equal(cause.SynapseId, request.CausationId);
        Assert.Equal(cause.CorrelationId, request.CorrelationId);
        Assert.Equal(request.SynapseId, received.SynapseId);
        Assert.Equal(request.CorrelationId, received.CorrelationId);
        Assert.Equal(request.CausationId, received.CausationId);
        Assert.Equal(request.Caller, received.Caller);
        Assert.Equal(request.Sequence, received.Sequence);
        Assert.Equal(request.Timestamp, received.Timestamp);

        var receivedCapability = Assert.IsType<CapabilityRequested>(received.Synapse);
        Assert.Equal(capability.Target, receivedCapability.Target);
        Assert.Equal(capability.Contract, receivedCapability.Contract);
        Assert.Equal(capability.Method, receivedCapability.Method);
    }
}

[Alias("db.test.ai-orchestration-probe")]
[ClientEntryPoint]
internal interface IAIOrchestrationProbe : INeuron
{
    [Alias("Call")]
    Task<ChatResponse> CallAsync(NeuronId target, IReadOnlyList<ChatMessage> messages);

    [Alias("ReadGroupState")]
    Task<byte[]> ReadGroupStateAsync(NeuronId target);

    [Alias("DeactivateGroup")]
    Task DeactivateGroupAsync(NeuronId target);

    [Alias("ChangeGroupParticipant")]
    Task ChangeGroupParticipantAsync(NeuronId target, string? name);

    [Alias("GroupActivation")]
    Task<Guid> GroupActivationAsync(NeuronId target);

    [Alias("ReadJournal")]
    Task<JournalRead> ReadJournalAsync(NeuronId target, JournalKind kind);
}

internal sealed class AIOrchestrationProbe : Neuron, IAIOrchestrationProbe
{
    public Task<ChatResponse> CallAsync(NeuronId target, IReadOnlyList<ChatMessage> messages)
        => GrainFactory.GetGrain<IAgent>(target.ToGrainId()).RespondAsync(messages);

    public Task<byte[]> ReadGroupStateAsync(NeuronId target)
        => GrainFactory.GetGrain<ITestGroupChat>(target.ToGrainId()).ReadSessionStateAsync();

    public Task DeactivateGroupAsync(NeuronId target)
        => GrainFactory.GetGrain<ITestGroupChat>(target.ToGrainId()).DeactivateAsync();

    public Task ChangeGroupParticipantAsync(NeuronId target, string? name)
        => GrainFactory.GetGrain<ITestGroupChat>(target.ToGrainId()).ChangeParticipantAsync(name);

    public Task<Guid> GroupActivationAsync(NeuronId target)
        => GrainFactory.GetGrain<ITestGroupChat>(target.ToGrainId()).ActivationAsync();

    public Task<JournalRead> ReadJournalAsync(NeuronId target, JournalKind kind)
        => GrainFactory.GetGrain<INeuron>(target.ToGrainId()).ReadJournalAsync(kind, afterSequence: 0);
}

[Alias("db.test.concurrent")]
internal interface ITestConcurrent : IAgent;

internal sealed class TestConcurrent : Concurrent, ITestConcurrent
{
    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<ILlama32>(),
        Participant<IGpt56>()
    ];
}

[Alias("db.test.invalid-concurrent")]
internal interface IInvalidConcurrent : IAgent;

internal sealed class InvalidConcurrent : Concurrent, IInvalidConcurrent
{
    protected override IReadOnlyList<Participant> Participants => [Participant<INeuron>()];
}

[Alias("db.test.group-chat")]
internal interface ITestGroupChat : IGroupChat
{
    [Alias("ReadSessionState")]
    Task<byte[]> ReadSessionStateAsync();

    [Alias("Deactivate")]
    Task DeactivateAsync();

    [Alias("ChangeParticipant")]
    Task ChangeParticipantAsync(string? name);

    [Alias("Activation")]
    Task<Guid> ActivationAsync();
}

internal sealed class TestGroupChat : GroupChat, ITestGroupChat
{
    private const string SessionStateName = "ai.group-chat.session";
    private readonly Guid _activation = Guid.NewGuid();

    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<ITestConcurrent>(GroupDefinitionSource.NameFor(Id.Owner)),
        Participant<IGpt56>()
    ];

    public Task<byte[]> ReadSessionStateAsync()
    {
        var state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(SessionStateName);

        return Task.FromResult(state.Value?.ToArray() ?? []);
    }

    public Task DeactivateAsync()
    {
        DeactivateOnIdle();

        return Task.CompletedTask;
    }

    public Task ChangeParticipantAsync(string? name)
    {
        GroupDefinitionSource.Set(Id.Owner, name);

        return Task.CompletedTask;
    }

    public Task<Guid> ActivationAsync() => Task.FromResult(_activation);

    public override Task AcceptAsync(AttemptRequest request) => throw new NotSupportedException();

    public override Task ContinueAsync(AttemptCursor cursor) => throw new NotSupportedException();

    public override Task CancelAsync(AttemptCursor cursor) => throw new NotSupportedException();
}

internal static class GroupDefinitionSource
{
    private static readonly ConcurrentDictionary<OwnerId, string> Names = new();

    internal static string? NameFor(OwnerId owner)
        => Names.GetValueOrDefault(owner);

    internal static void Set(OwnerId owner, string? name)
    {
        if (name is null)
        {
            Names.TryRemove(owner, out _);

            return;
        }

        Names[owner] = name;
    }
}

internal sealed class OrchestrationChatClient : IChatClient
{
    private readonly ConcurrentQueue<IReadOnlyList<ChatMessage>> _calls = new();
    private readonly Func<IReadOnlyList<ChatMessage>, string> _answer;

    internal OrchestrationChatClient(string answer)
        : this(_ => answer)
    {
    }

    internal OrchestrationChatClient(Func<IReadOnlyList<ChatMessage>, string> answer)
    {
        _answer = answer;
    }

    internal IReadOnlyList<IReadOnlyList<ChatMessage>> Calls => [.. _calls];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = messages.ToArray();
        _calls.Enqueue(request);

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _answer(request))));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);

        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
