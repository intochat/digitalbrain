using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using Microsoft.Extensions.AI;

namespace DigitalBrain.ModuleTests;

public static class ProbeParticipants
{
    public const string Left = "left";
    public const string Right = "right";
}

[Alias("DigitalBrain.ModuleTests.IConcurrentProbe")]
public interface IConcurrentProbe : IAgent;

[Alias("DigitalBrain.ModuleTests.IParticipantSwapConcurrentProbe")]
[ClientEntryPoint]
public interface IParticipantSwapConcurrentProbe : IAgent
{
    [Alias(nameof(UseParticipants))]
    Task UseParticipants(string left, string right);
}

[Alias("DigitalBrain.ModuleTests.IUnreachableGroupChatProbe")]
public interface IUnreachableGroupChatProbe : IGroupChat;

[Alias("DigitalBrain.ModuleTests.ISilentParticipantProbe")]
public interface ISilentParticipantProbe : IAgent;

[Alias("DigitalBrain.ModuleTests.ISilentSwapConcurrentProbe")]
[ClientEntryPoint]
public interface ISilentSwapConcurrentProbe : IAgent
{
    [Alias(nameof(UseParticipant))]
    Task UseParticipant(string name);
}

[Alias("DigitalBrain.ModuleTests.IGroupChatProbe")]
[ClientEntryPoint]
public interface IGroupChatProbe : IGroupChat
{
    [Alias(nameof(InvokeAccept))]
    Task InvokeAccept(AttemptRequest request);

    [Alias(nameof(InvokeContinue))]
    Task InvokeContinue(AttemptCursor cursor);

    [Alias(nameof(InvokeCancel))]
    Task InvokeCancel(AttemptCursor cursor);
}

public sealed class ConcurrentProbe : Concurrent, IConcurrentProbe
{
    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<ILlama32>(ProbeParticipants.Left),
        Participant<ILlama32>(ProbeParticipants.Right),
    ];
}

public sealed class ParticipantSwapConcurrentProbe : Concurrent, IParticipantSwapConcurrentProbe
{
    private string _left = ProbeParticipants.Left;
    private string _right = ProbeParticipants.Right;

    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<ILlama32>(_left),
        Participant<ILlama32>(_right),
    ];

    public Task UseParticipants(string left, string right)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(left);
        ArgumentException.ThrowIfNullOrWhiteSpace(right);
        _left = left;
        _right = right;
        return Task.CompletedTask;
    }
}

public sealed class GroupChatProbe : GroupChat, IGroupChatProbe
{
    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<ILlama32>(ProbeParticipants.Left),
        Participant<ILlama32>(ProbeParticipants.Right),
    ];

    public Task InvokeAccept(AttemptRequest request) => Accept(request);

    public Task InvokeContinue(AttemptCursor cursor) => Continue(cursor);

    public Task InvokeCancel(AttemptCursor cursor) => Cancel(cursor);
}

public sealed class UnreachableGroupChatProbe : GroupChat, IUnreachableGroupChatProbe
{
    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<IGpt56>(ProbeParticipants.Left),
        Participant<IGpt56>(ProbeParticipants.Right),
    ];
}

public sealed class SilentParticipantProbe : Neuron, ISilentParticipantProbe
{
    internal const string ParticipantName = "silent-participant";

    public IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        return AsyncEnumerable.Empty<ChatResponseUpdate>();
    }

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return RespondStreaming(messages).ToChatResponseAsync();
    }
}

public sealed class SilentSwapConcurrentProbe : Concurrent, ISilentSwapConcurrentProbe
{
    private string _participant = SilentParticipantProbe.ParticipantName;

    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<ISilentParticipantProbe>(_participant),
    ];

    public Task UseParticipant(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _participant = name;
        return Task.CompletedTask;
    }
}

[GenerateSerializer]
[Alias("moduletests.probe-goal")]
public sealed record ProbeGoal([property: Id(0)] string Text) : Goal;
