using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.AI;

[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "GroupChat is the ratified public orchestration vocabulary.")]
public abstract class GroupChat : Neuron, IGroupChat
{
    private const string ProtectionPurpose = "DigitalBrain.AI.GroupChat.AgentSession.v1";
    private const string StateName = "ai.group-chat.session";
    private readonly IDurableValue<byte[]> _state;

    protected GroupChat()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
    }

    protected abstract IReadOnlyList<Participant> Participants { get; }

    protected Participant<TNeuron> Participant<TNeuron>(string? name = null)
        where TNeuron : INeuron
        => new(NeuronId.For<TNeuron>(Id.Owner, name ?? Id.Name));

    public abstract Task AcceptAsync(AttemptRequest request);

    public abstract Task ContinueAsync(AttemptCursor cursor);

    public abstract Task CancelAsync(AttemptCursor cursor);

    public async Task<ChatResponse> RespondAsync(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var snapshot = Participants.ToArray();
        var definition = SessionCompatibility.Describe(GetType(), snapshot);
        var protector = ServiceProvider
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(ProtectionPurpose, Id.ToString(), definition.Fingerprint);
        var turnScheduler = TaskScheduler.Current;
        var participants = MafParticipantAdapter.CreateAll(GrainFactory, snapshot, turnScheduler);
        var workflow = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(team => new RoundRobinGroupChatManager(team)
            {
                MaximumIterationCount = snapshot.Length,
            })
            .AddParticipants(participants)
            .Build();
        var agent = workflow.AsAIAgent(
            id: definition.HostId,
            name: definition.HostName,
            description: null,
            executionEnvironment: InProcessExecution.Lockstep,
            includeExceptionDetails: false,
            includeWorkflowOutputsInResponse: false);
        var session = _state.Value is { Length: > 0 } serialized
            ? await RestoreAsync(agent, serialized, definition, protector)
            : await agent.CreateSessionAsync();
        var response = await agent.RunAsync(messages, session);
        var serializedSession = await agent.SerializeSessionAsync(session);
        var protectedSession = protector.Protect(Encoding.UTF8.GetBytes(serializedSession.GetRawText()));
        var envelope = new OrchestrationState(
            definition.FormatVersion,
            definition.MafVersion,
            definition.Fingerprint,
            definition.Participants,
            protectedSession);

        _state.Value = JsonSerializer.SerializeToUtf8Bytes(envelope);
        await WriteStateAsync();

        return response.AsChatResponse();
    }

    private static async Task<AgentSession> RestoreAsync(
        AIAgent agent,
        byte[] serialized,
        OrchestrationDefinition definition,
        IDataProtector protector)
    {
        OrchestrationState stored;

        try
        {
            stored = JsonSerializer.Deserialize<OrchestrationState>(serialized)
                ?? throw RecoveryRequired();
        }
        catch (Exception failure) when (failure is JsonException or NotSupportedException)
        {
            throw RecoveryRequired(failure);
        }

        SessionCompatibility.RequireMatch(stored, definition);

        try
        {
            var sessionBytes = protector.Unprotect(stored.ProtectedSession);
            using var sessionJson = JsonDocument.Parse(sessionBytes);

            return await agent.DeserializeSessionAsync(sessionJson.RootElement.Clone());
        }
        catch (Exception failure) when (failure is CryptographicException
            or JsonException
            or FormatException
            or InvalidOperationException)
        {
            throw RecoveryRequired(failure);
        }
    }

    private static InvalidOperationException RecoveryRequired(Exception? failure = null)
        => new(
            "The durable group-chat session cannot be restored; an explicit migration or reset is required.",
            failure);
}
