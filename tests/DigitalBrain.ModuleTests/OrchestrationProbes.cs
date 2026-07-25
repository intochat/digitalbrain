using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Tasks;

namespace DigitalBrain.ModuleTests;

[Alias("DigitalBrain.ModuleTests.IConcurrentProbe")]
public interface IConcurrentProbe : IAgent;

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
        Participant<ILlama32>("left"),
        Participant<ILlama32>("right"),
    ];
}

public sealed class GroupChatProbe : GroupChat, IGroupChatProbe
{
    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<ILlama32>("left"),
        Participant<ILlama32>("right"),
    ];

    public Task InvokeAccept(AttemptRequest request) => Accept(request);

    public Task InvokeContinue(AttemptCursor cursor) => Continue(cursor);

    public Task InvokeCancel(AttemptCursor cursor) => Cancel(cursor);
}

[GenerateSerializer]
[Alias("moduletests.probe-goal")]
public sealed record ProbeGoal([property: Id(0)] string Text) : Goal;
