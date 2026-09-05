using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using DigitalBrain.Product.Interactions;
using Microsoft.Extensions.AI;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

[Alias("DigitalBrain.Simulation.Tests.IProbe")]
public interface IProbe : IAgent;

[GrainType("probe")]
internal sealed class Probe(NeuronRuntime runtime, IChatClient client) : Agent(runtime, client), IProbe
{
    internal const string Purpose = "You are the integration-test specialist probe.";

    protected override string Instructions => Purpose;
    protected override string DisplayName => "Probe";
    protected override IReadOnlyList<AITool> Tools =>
    [
        AIFunctionFactory.Create(() => "fresh probe evidence", new AIFunctionFactoryOptions { Name = "probe_read" }),
    ];
}

internal sealed class CapturingDelegation : IAgentToolSource
{
    private readonly AgentDelegation<IProbe> _delegation = new(
        "ask_probe", "Delegate an investigation to the probe specialist.", "application");

    public ConcurrentQueue<AgentToolContext> Contexts { get; } = new();

    public IReadOnlyList<AIFunction> ToolsFor(AgentToolContext context)
    {
        Contexts.Enqueue(context);
        return _delegation.ToolsFor(context);
    }
}

internal sealed record ProbeModelCall(
    PrincipalId Principal,
    NeuronId? Chat,
    CommandId? Command,
    string Request,
    string[] ToolNames,
    string Reply);

// Invokes the same AIFunction objects a provider's function-calling client uses.
// Replies have a per-principal sequence so accidentally reusing an earlier reply
// or leaking another principal's selected target cannot pass these tests.
internal sealed class DelegationChatClient(bool pauseFirstProbe = false) : IChatClient
{
    private readonly ConcurrentDictionary<PrincipalId, int> _counts = new();
    private int _firstProbe;

    public ConcurrentQueue<ProbeModelCall> ProbeCalls { get; } = new();
    public ConcurrentQueue<string[]> AssistantTools { get; } = new();
    public TaskCompletionSource ProbeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ContinueProbe { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var history = messages.ToArray();
        var names = options?.Tools?.Select(tool => tool.Name).ToArray() ?? [];
        string response;
        if (history.Any(message => message.Role == ChatRole.System && message.Text == Probe.Purpose))
        {
            var actor = Assert.IsType<ActorContext>(VerifiedActor.Current);
            var turn = AgentTurnContext.Current;
            var ordinal = _counts.AddOrUpdate(actor.PrincipalId, 1, static (_, count) => count + 1);
            if (Interlocked.Exchange(ref _firstProbe, 1) == 0)
            {
                ProbeStarted.TrySetResult();
                if (pauseFirstProbe)
                {
                    await ContinueProbe.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
                }
            }

            var tool = Assert.Single(options!.Tools!.OfType<AIFunction>(), candidate => candidate.Name == "probe_read");
            var evidence = await tool.InvokeAsync([], cancellationToken).ConfigureAwait(true);
            response = Reply(actor.PrincipalId, ordinal);
            Assert.Contains("fresh probe evidence", evidence?.ToString() ?? "", StringComparison.Ordinal);
            ProbeCalls.Enqueue(new ProbeModelCall(actor.PrincipalId, turn?.Chat, turn?.CommandId,
                history.Last(message => message.Role == ChatRole.User).Text, names, response));
        }
        else
        {
            AssistantTools.Enqueue(names);
            var tool = Assert.Single(options!.Tools!.OfType<AIFunction>(), candidate => candidate.Name == "ask_probe");
            var results = new List<string>();
            for (var call = 0; call < 2; call++)
            {
                var result = await tool.InvokeAsync(new AIFunctionArguments { ["request"] = "check status" }, cancellationToken)
                    .ConfigureAwait(true);
                results.Add(result?.ToString() ?? "");
            }
            response = string.Join(" | ", results);
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, response) { FinishReason = ChatFinishReason.Stop };
    }

    internal static string Reply(PrincipalId principal, int ordinal) => $"probe:{principal.Value:N}:{ordinal}";

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
