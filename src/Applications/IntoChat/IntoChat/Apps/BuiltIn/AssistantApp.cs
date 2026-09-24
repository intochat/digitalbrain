using DigitalBrain.AI;
using DigitalBrain.AI.Agents;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Button.Signals;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Flutter.TextField;
using IntoChat.Agent;
using Orleans.Metadata;
using Orleans.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IntoChat.Apps.BuiltIn;

[GenerateSerializer, Alias("intochat.assistant-app-state")]
public sealed record AssistantAppState
{
    [Id(0)] public long Revision { get; init; }
    [Id(1)] public bool Active { get; init; }
    [Id(2)] public AgentDefinition Agent { get; init; } = new();
    [Id(3)] public UiChildRef Surface { get; init; } = new("surface", "");
    [Id(4)] public UiChildRef Input { get; init; } = new("textfield", "");
    [Id(5)] public UiChildRef Response { get; init; } = new("text", "");
    [Id(6)] public string AgentId { get; init; } = "";
}

[Alias("intochat.assistant-app"), DefaultGrainType("intochat.assistant-app")]
public interface IAssistantApp : INeuron
{
    Task<AssistantAppState> Activate();
    Task<AssistantAppState> Read();
    Task<AssistantAppState> Configure(AgentDefinition definition);
    Task<IAgent> Conversation(string threadId);
    Task<AgentResponse> Send(string message);
    Task<AgentResponse> SendDraft();
}

[GrainType("intochat.assistant-app")]
public sealed class AssistantApp(
    [PersistentState("intochat.assistant-app", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AssistantAppState> store)
    : App<AssistantAppState>(store), IAssistantApp, IAppDefinition, INeuronObserver
{
    private IGrainTimer? _renewal;
    public static AppDefinition Definition => new("intochat.assistant", [typeof(AIModule), typeof(FlutterModule)], typeof(IAssistantApp));

    public async Task<AssistantAppState> Activate()
    {
        if (Snapshot.Active) { return Snapshot; }
        var prefix = this.GetPrimaryKeyString() + "/apps/assistant";
        var agentId = prefix + "/agent";
        var agent = GrainFactory.GetGrain<IAgent>(agentId);
        await agent.Configure(Snapshot.Agent, (await agent.GetState()).Revision);
        var input = GrainFactory.GetGrain<ITextField>(prefix + "/input");
        await input.Configure("Message the assistant", "text");
        var send = GrainFactory.GetGrain<IButton>(prefix + "/send");
        await send.Set("Send", "assistant.send");
        var response = GrainFactory.GetGrain<IText>(prefix + "/response");
        await response.Set("");
        var layout = GrainFactory.GetGrain<ILayout>(prefix + "/layout");
        await layout.Set(new("column", [new("text", prefix + "/response"), new("textfield", prefix + "/input"), new("button", prefix + "/send")]), (await layout.Read()).Revision);
        var surface = GrainFactory.GetGrain<ISurface>(prefix + "/surface");
        await surface.Set(new("Assistant", [new("layout", prefix + "/layout")]), (await surface.Read()).Revision);
        var next = Snapshot with
        {
            Active = true, Revision = Snapshot.Revision + 1, AgentId = agentId,
            Surface = new("surface", prefix + "/surface"), Input = new("textfield", prefix + "/input"), Response = new("text", prefix + "/response"),
        };
        await Save(next, new BuiltInAppChanged(Definition.Id, next.Revision));
        await Subscribe();
        return Snapshot;
    }

    public Task<AssistantAppState> Read() => Task.FromResult(Snapshot);

    public async Task<AssistantAppState> Configure(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        await Activate();
        var agent = GrainFactory.GetGrain<IAgent>(Snapshot.AgentId);
        await agent.Configure(definition, (await agent.GetState()).Revision);
        var next = Snapshot with { Agent = definition, Revision = Snapshot.Revision + 1 };
        await Save(next, new BuiltInAppChanged(Definition.Id, next.Revision));
        return Snapshot;
    }

    public async Task<IAgent> Conversation(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId) || threadId.Length > 200 || threadId.Any(char.IsControl) || threadId.Contains('/') || threadId.Contains('\\'))
        { throw new ArgumentException("A bounded thread identifier is required.", nameof(threadId)); }
        await Activate();
        // Keep existing conversations addressable when the host delegates ownership to this app.
        return GrainFactory.GetGrain<IAgent>(AgentEndpoints.ConversationKey(this.GetPrimaryKeyString(), threadId));
    }

    public async Task<AgentResponse> Send(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 32000)
        { throw new ArgumentException("A message of at most 32000 characters is required.", nameof(message)); }
        await Activate();
        var result = await GrainFactory.GetGrain<IAgent>(Snapshot.AgentId).GetRichResponse(message);
        await GrainFactory.GetGrain<IText>(Snapshot.Response.Name).Set(result.Text);
        return result;
    }

    public async Task<AgentResponse> SendDraft()
    {
        await Activate();
        var input = GrainFactory.GetGrain<ITextField>(Snapshot.Input.Name);
        var result = await Send((await input.Read()).Value);
        await input.SetValue("");
        return result;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        if (Snapshot.Active) { await Subscribe(); }
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _renewal?.Dispose();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    Task INeuronObserver.OnSignalAsync(Signal signal) =>
        signal is ButtonClicked clicked && clicked.Name == this.GetPrimaryKeyString() + "/apps/assistant/send" && clicked.Action == "assistant.send"
            ? SendDraft() : Task.CompletedTask;

    private async Task Subscribe()
    {
        var button = GrainFactory.GetGrain<IButton>(this.GetPrimaryKeyString() + "/apps/assistant/send");
        var observer = this.AsReference<INeuronObserver>();
        await button.Watch(observer);
        if (_renewal is not null) { return; }
        var interval = ServiceProvider.GetRequiredService<IOptions<BrainOptions>>().Value.RenewEvery;
        _renewal = this.RegisterGrainTimer((_, _) => button.Watch(observer), 0,
            new GrainTimerCreationOptions { DueTime = interval, Period = interval, Interleave = true, KeepAlive = true });
    }
}
