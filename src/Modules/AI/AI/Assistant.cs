using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.AI.Ollama;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Assistant;
internal sealed class Assistant([FromKeyedServices(typeof(Gemma4))] IChatClient chatClient)
    : Agent(chatClient), IAssistant
{
    public const string ConveneModelTeam = "convene_model_team";

    private const int SmallestTeam = 2;

    private static readonly string ModelRoster = string.Join(", ", TeamLineUp.KnownModels);

    protected override string? Instructions =>
        $$"""
        You are DigitalBrain, the AI assistant inside the DigitalBrain personal agent system.
        Be precise about your identity and never invent tools, integrations, or completed actions.

        The system you live in is a graph of neurons. Every neuron has an identity written as
        type:owner/name, and every neuron you can touch belongs to owner '{{Id.Owner.Value}}'.
        Neurons emit facts called synapses, and the synapse graph decides which neurons hear
        them: a connection routes one neuron's emitted synapse (matched by its alias, for
        example 'chat.responded' or 'ui.chart-point') to a target neuron, optionally reshaped
        by a transform. When the owner asks you to connect, route, track, or watch something,
        use your tools: read the topology first to find real neuron identities and live
        connections, then create or remove connections. A NeuronId argument is an object of
        the form {"type": "chart", "owner": {"value": "{{Id.Owner.Value}}"}, "name": "dashboard"}.
        Transforms can be named, or written as data: 'to:ui.chart-point{Label=Text}' reshapes
        any synapse into a chart point by copying its Text field into Label. A connectionId
        may be a short stable name such as 'chat-to-dashboard'; the same name always
        addresses the same connection, so reusing it replaces, and disconnecting by it removes. Chart neurons
        (type 'chart') render 'ui.chart-point' synapses; connect a source to a chart to see
        its facts live on a dashboard.

        Timer neurons (type 'timer') arm a countdown with the time.start-timer tool. When the
        owner asks for a timer or reminder, first route the timer's facts into this chat:
        connect the timer to the chat for alias 'time.timer-scheduled' with transform
        'to:ui.timer-card{Label=Note,DueAt=DueAt}' so a live clock card appears, and for
        alias 'time.timer-elapsed' with transform 'to:ui.note{Text=Note}' so the note lands
        in chat when time is up. Then start the timer with the duration in seconds and the
        note to deliver; cancel it with time.cancel-timer.

        You can provide general conversational help using the current chat context. External
        capabilities are discovered from the active capability catalog for this deployment —
        only call tools that are actually offered on the current turn. Provider modules own
        Gmail, Salesforce, and other integrations; do not invent provider-specific tool names
        or claim actions that were not returned by a tool.

        A local action, convene_model_team, is offered to you only while the owner's latest
        message names at least {{SmallestTeam}} of the models this deployment runs ({{ModelRoster}}).
        When it is absent from your tools, ask the owner which models to compare instead of
        pretending you asked them. Never claim you convened a team you were not offered.

        Do not call a tool merely to describe your capabilities. If authentication or a provider
        is unavailable, report that limitation.
        """;

    protected override IReadOnlyList<AIFunction> AdditionalToolsFor(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var tools = new List<AIFunction>(CoreSystemTools());

        if (ModelMentions.NamedIn(LatestOwnerText(messages)).Count >= SmallestTeam)
        {
            tools.Add(ConveneTool());
        }

        return tools;
    }

    private IReadOnlyList<AIFunction> CoreSystemTools()
    {
        var catalog = ServiceProvider.GetService<ActiveCapabilityCatalog>();
        var typeMap = ServiceProvider.GetService<ActiveModuleContractTypeMap>();
        if (catalog is null || typeMap is null)
        {
            return [];
        }

        List<AIFunction> tools = [];
        foreach (var neuronContract in (string[])["db.synapse-graph", "introspection"])
        {
            if (!catalog.TryGetNeuron(neuronContract, out var neuron) || neuron is null)
            {
                continue;
            }

            foreach (var accepted in neuron.Accepted)
            {
                var capability = new ValidatedCapability(
                    CapabilityKinds.Synapse,
                    ValidatedCapability.ToolNameFor(accepted.ContractId, accepted.SchemaVersion),
                    accepted.ContractId,
                    accepted.SchemaVersion,
                    neuron.ContractId,
                    neuron.DefaultInstanceName,
                    accepted.Description,
                    accepted.JsonSchema);

                try
                {
                    tools.Add(SynapseCapabilityTool.Materialize(capability, GrainFactory, Id.Owner, typeMap));
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        return tools;
    }

    private AIFunction ConveneTool()
        => Capability(
                ConveneModelTeam,
                "Put two or more named models in one room, ask them a single question, and return what "
                + "they jointly answered. Use this only when the owner explicitly asks to compare, "
                + "contrast, or get a second opinion from named models. Name each model exactly as one "
                + $"of: {ModelRoster}.",
                ([Description("Two or more model names, each exactly as listed in this tool's description")] string[] models,
                 [Description("The single question all of those models should answer")] string question)
                    => ConveneAsync(models, question));

    private static string LatestOwnerText(IReadOnlyList<ChatMessage> messages)
    {
        for (var turn = messages.Count - 1; turn >= 0; turn--)
        {
            if (messages[turn].Role == ChatRole.User)
            {
                return messages[turn].Text;
            }
        }

        return string.Empty;
    }

    private async Task<string> ConveneAsync(string[] models, string question)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(models);
            ArgumentException.ThrowIfNullOrWhiteSpace(question);

            if (models.Length < SmallestTeam)
            {
                return $"A team answers together, so it needs at least {SmallestTeam} models. "
                    + $"Name each one exactly as one of: {ModelRoster}.";
            }

            var lineUp = TeamLineUp.Of(models);
            var team = GrainFactory.GetGrain<ITeam>(
                NeuronId.For<ITeam>(Id.Owner, lineUp.TeamName).ToGrainId());

            await team.Form(lineUp.Formation).ConfigureAwait(true);

            var answer = await team.Respond([new ChatMessage(ChatRole.User, question)])
                .ConfigureAwait(true);

            return answer.Text;
        }
        catch (ArgumentException correctable)
        {
            return Correctable(correctable);
        }
        catch (OrchestrationRefusedException correctable)
        {
            return Correctable(correctable);
        }
    }

    private static string Correctable(Exception failure)
        => failure.InnerException is { } cause
            ? $"{failure.Message} Cause: {cause.Message}"
            : failure.Message;
}
