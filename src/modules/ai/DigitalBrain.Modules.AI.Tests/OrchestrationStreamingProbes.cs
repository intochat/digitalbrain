using System.Runtime.CompilerServices;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;
[Alias("DigitalBrain.ModuleTests.IGatedParticipantProbe")]
public partial interface IGatedParticipantProbe : IAgent;

[Alias("DigitalBrain.ModuleTests.IStreamingConcurrentProbe")]
public partial interface IStreamingConcurrentProbe : IAgent;

[Alias("DigitalBrain.ModuleTests.IStreamingGroupChatProbe")]
public partial interface IStreamingGroupChatProbe : IGroupChat;

[Alias("DigitalBrain.ModuleTests.IHeldFirstGroupChatProbe")]
public partial interface IHeldFirstGroupChatProbe : IGroupChat;

[Alias("DigitalBrain.ModuleTests.IFragmentedParticipantProbe")]
public partial interface IFragmentedParticipantProbe : IAgent;

[Alias("DigitalBrain.ModuleTests.IFragmentedConcurrentProbe")]
public partial interface IFragmentedConcurrentProbe : IAgent;

public sealed class FragmentedParticipantProbe : Neuron, IFragmentedParticipantProbe
{
    internal const string ParticipantName = "fragmented-participant";
    internal const string FirstFragment = "first-fragment";
    internal const string SecondFragment = "second-fragment";

    private static readonly TimeSpan HoldBudget = TimeSpan.FromSeconds(60);

    private static TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static void Arm() => _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static void Release() => _released.TrySetResult();

    public async IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var release = _released.Task;

        yield return new ChatResponseUpdate(ChatRole.Assistant, FirstFragment);

        await release.WaitAsync(HoldBudget, cancellationToken);

        yield return new ChatResponseUpdate(ChatRole.Assistant, SecondFragment);
    }

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return RespondStreaming(messages).ToChatResponseAsync();
    }
}

public sealed class FragmentedConcurrentProbe : Concurrent, IFragmentedConcurrentProbe
{
    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<IFragmentedParticipantProbe>(FragmentedParticipantProbe.ParticipantName),
    ];
}

public sealed class GatedParticipantProbe : Neuron, IGatedParticipantProbe
{
    internal const string HeldName = "gated-held";
    internal const string ImmediateName = "gated-immediate";
    internal const string HeldReply = "held-participant-reply";
    internal const string ImmediateReply = "immediate-participant-reply";

    private static readonly TimeSpan HoldBudget = TimeSpan.FromSeconds(60);

    private static TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource _heldParticipantEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static int _immediateParticipantCalls;

    internal static Task HeldParticipantEntered => _heldParticipantEntered.Task;

    internal static int ImmediateParticipantCalls => Volatile.Read(ref _immediateParticipantCalls);

    internal static void Arm()
    {
        _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _heldParticipantEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _immediateParticipantCalls, 0);
    }

    internal static void Release() => _released.TrySetResult();

    public async Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (!string.Equals(Id.Name, HeldName, StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _immediateParticipantCalls);

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, ImmediateReply));
        }

        var release = _released.Task;
        _heldParticipantEntered.TrySetResult();
        await release.WaitAsync(HoldBudget);

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, HeldReply));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await Respond(messages).WaitAsync(cancellationToken);

        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }
}

public sealed class StreamingConcurrentProbe : Concurrent, IStreamingConcurrentProbe
{
    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<IGatedParticipantProbe>(GatedParticipantProbe.ImmediateName),
        Participant<IGatedParticipantProbe>(GatedParticipantProbe.HeldName),
    ];
}

public sealed class StreamingGroupChatProbe : GroupChat, IStreamingGroupChatProbe
{
    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<IGatedParticipantProbe>(GatedParticipantProbe.ImmediateName),
        Participant<IGatedParticipantProbe>(GatedParticipantProbe.HeldName),
    ];
}

public sealed class HeldFirstGroupChatProbe : GroupChat, IHeldFirstGroupChatProbe
{
    protected override IReadOnlyList<Participant> Participants =>
    [
        Participant<IGatedParticipantProbe>(GatedParticipantProbe.HeldName),
        Participant<IGatedParticipantProbe>(GatedParticipantProbe.ImmediateName),
    ];
}
