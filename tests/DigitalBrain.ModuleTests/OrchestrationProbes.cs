using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Tasks;

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

[GenerateSerializer]
[Alias("moduletests.probe-goal")]
public sealed record ProbeGoal([property: Id(0)] string Text) : Goal;
