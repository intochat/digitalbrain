using System.Runtime.CompilerServices;
using DigitalBrain.AI;
using DigitalBrain.Abstractions;
using DigitalBrain.Tests.Harness;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Tests.Harness;

[GrainType("wiringagent")]
internal sealed class WiringAgent : Agent
{
    private readonly ScriptedBindingChatClient _script;

    public WiringAgent()
        : this(new ScriptedBindingChatClient())
    {
    }

    private WiringAgent(ScriptedBindingChatClient script)
        : base(script)
        => _script = script;

    protected override Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
    {
        _script.Owner = Id.Owner.Value;
        return Task.CompletedTask;
    }
}

internal sealed class ScriptedBindingChatClient : IChatClient
{
    internal string Owner { get; set; } = "unowned";

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;

        if (messages.Any(static message => message.Role == ChatRole.Tool))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "wired elon to the dashboard");
            yield break;
        }

        var bindTool = options?.Tools?.OfType<AIFunction>()
            .FirstOrDefault(tool => tool.Name == ValidatedCapability.ToolNameFor("db.bind", 1));
        if (bindTool is null)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "no wiring tool was offered");
            yield break;
        }

        var owner = new OwnerId(Owner);
        var arguments = new Dictionary<string, object?>
        {
            ["bindingId"] = Guid.NewGuid(),
            ["source"] = new NeuronId("probesource", owner, "elon"),
            ["synapseAlias"] = "probe.fact",
            ["target"] = new NeuronId("chart", owner, "dashboard"),
            ["transform"] = ProbeFactToChartPoint.TransformName,
        };

        yield return new ChatResponseUpdate(
            ChatRole.Assistant,
            [new FunctionCallContent("wire-1", bindTool.Name, arguments)]);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
