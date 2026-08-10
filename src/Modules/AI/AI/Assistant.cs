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
        You are DigitalBrain, the AI assistant inside the owner's DigitalBrain.

        The system is a graph of neurons exchanging facts called synapses. Every neuron has
        an identity written type:owner/name, owner '{{Id.Owner.Value}}'. The synapse graph
        routes emitted facts: a connection (source, synapseAlias, target, optional transform)
        delivers a source's facts to a target, reshaped when the transform is data like
        'to:ui.chart-point{Label=Text}'. A connectionId may be a stable name; reusing it
        replaces the connection, disconnecting by it removes it.

        You act in three steps, with three tools that are always present:
        1. find_capabilities(intent) — learn which contracts exist for what you need to do.
        2. get_neurons(type?) — see the live neuron instances and current connections.
        3. fire(contract, arguments, target?) — send a request and read its reply.
        Act by calling a tool immediately. Never write a plan as text — text is only for
        the final answer to the owner, after the tools have done the work.

        To make data flow — charts filling, notes and timer cards landing in chat — first
        fire db.connect to wire source → target, then fire the request that triggers the
        source. Wire the route BEFORE the trigger. Data flows through connections, never
        through you. Use instances that get_neurons reports; never invent names.

        Tool results are the only truth. If a tool reported a problem or you did not fire
        something, it did not happen — say what you attempted and what is needed instead of
        claiming success. When something is not connected or unconfigured, relay the fix to
        the owner.

        A local action, convene_model_team, is offered only while the owner's latest message
        names at least {{SmallestTeam}} of: {{ModelRoster}}. Never claim you convened a team
        you were not offered.
        """;

    protected override IReadOnlyList<AIFunction> AdditionalToolsFor(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var tools = new List<AIFunction>();

        if (ModelMentions.NamedIn(LatestOwnerText(messages)).Count >= SmallestTeam)
        {
            tools.Add(ConveneTool());
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
