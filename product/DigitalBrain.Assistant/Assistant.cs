using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Assistant;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Orleans grain activated by the silo from IAssistant grain identity.")]
internal sealed class Assistant([FromKeyedServices(typeof(Gemma4))] IChatClient chatClient)
    : Agent(chatClient), IAssistant
{
    public const string ConveneModelTeam = "convene_model_team";

    private const int SmallestTeam = 2;

    private static readonly string ModelRoster = string.Join(", ", TeamLineUp.KnownModels);

    protected override string? Instructions =>
        $"""
        You are DigitalBrain, the AI assistant inside the DigitalBrain personal agent system.
        Be precise about your identity and never invent tools, integrations, or completed actions.

        You can provide general conversational help using the current chat context. External
        capabilities are discovered from the active capability catalog for this deployment —
        only call tools that are actually offered on the current turn. Provider modules own
        Gmail, Salesforce, and other integrations; do not invent provider-specific tool names
        or claim actions that were not returned by a tool.

        A local action, convene_model_team, is offered to you only while the owner's latest
        message names at least {SmallestTeam} of the models this deployment runs ({ModelRoster}).
        When it is absent from your tools, ask the owner which models to compare instead of
        pretending you asked them. Never claim you convened a team you were not offered.

        Do not call a tool merely to describe your capabilities. If authentication or a provider
        is unavailable, report that limitation.
        """;

    protected override IReadOnlyList<AIFunction> AdditionalToolsFor(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (ModelMentions.NamedIn(LatestOwnerText(messages)).Count < SmallestTeam)
        {
            return [];
        }

        return
        [
            Capability(
                ConveneModelTeam,
                "Put two or more named models in one room, ask them a single question, and return what "
                + "they jointly answered. Use this only when the owner explicitly asks to compare, "
                + "contrast, or get a second opinion from named models. Name each model exactly as one "
                + $"of: {ModelRoster}.",
                ([Description("Two or more model names, each exactly as listed in this tool's description")] string[] models,
                 [Description("The single question all of those models should answer")] string question)
                    => ConveneAsync(models, question)),
        ];
    }

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

            await team.Form(lineUp.Formation);

            var answer = await team.Respond([new ChatMessage(ChatRole.User, question)]);

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
