using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.OS;

internal sealed class Assistant([FromKeyedServices(typeof(Gemma4))] IChatClient chatClient)
    : Agent(chatClient), IAssistant
{
    public const string EnrichAccountFromEmail = "enrich_account_from_email";
    public const string ConveneModelTeam = "convene_model_team";
    public const string DefaultGmailAccount = "gmail";
    public const string DefaultSalesforceAccount = "salesforce";

    private const int SmallestTeam = 2;

    private static readonly string ModelRoster = string.Join(", ", TeamLineUp.KnownModels);

    protected override string? Instructions =>
        $"""
        You are DigitalBrain, the AI assistant inside the DigitalBrain personal agent system.
        Be precise about your identity and never invent tools, integrations, or completed actions.

        You can provide general conversational help using the current chat context. In this
        deployment you have one always-available external action: enrich_account_from_email. Given
        an exact Gmail message ID and a 15- or 18-character Salesforce Account ID, it reads that
        specific Gmail message and creates a durable proposal to replace that Account's Description.
        The proposal is not written to Salesforce until the owner separately gives explicit approval.

        A second action, convene_model_team, is offered to you only while the owner's latest message
        names at least {SmallestTeam} of the models this deployment runs ({ModelRoster}). When it is
        absent from your tools, ask the owner which models to compare instead of pretending you asked
        them. Never claim you convened a team you were not offered.

        You cannot search or list Gmail, send email, directly update Salesforce, approve a proposal,
        or create accounts, contacts, or leads. Ask for both required IDs when either is missing.
        Do not call a tool merely to describe your capabilities. Never describe a proposal as a
        completed update. If authentication or a provider is unavailable, report that limitation.
        """;

    protected override IReadOnlyList<CapabilityTool> ToolsFor(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var enrichment = Capability(
            EnrichAccountFromEmail,
            "Given an exact Gmail message ID and a 15- or 18-character Salesforce Account ID, "
            + "read only that message and create an approval-gated Account Description proposal. "
            + "This cannot search Gmail or directly write Salesforce.",
            ([Description("Salesforce account id, 15 or 18 characters")] string accountId,
             [Description("Gmail message id to read")] string messageId)
                => ProposeEnrichmentAsync(accountId, messageId));

        if (ModelMentions.NamedIn(LatestOwnerText(messages)).Count < SmallestTeam)
        {
            return [enrichment];
        }

        return
        [
            enrichment,
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

    private async Task<string> ProposeEnrichmentAsync(string accountId, string messageId)
    {
        ValidateAccountId(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var message = await Gmail().ReadMessage(messageId, CancellationToken.None);
        var mutation = await Salesforce().ProposeAccountDescription(
            CommandId.New(),
            Id,
            accountId,
            $"Email from {message.Sender}: {message.Subject}\n{message.PlaintextBody}",
            CancellationToken.None);

        return $"Proposed a description for account {accountId} from message {messageId}. "
            + $"It is {mutation.State} and will not be written until you approve it.";
    }

    private static void ValidateAccountId(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        if (accountId.Length is not (15 or 18)
            || accountId.Any(static character =>
                !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("A Salesforce Account ID must be a 15- or 18-character alphanumeric value.", nameof(accountId));
        }
    }

    private IGmail Gmail()
        => GrainFactory.GetGrain<IGmail>(NeuronId.For<IGmail>(Id.Owner, DefaultGmailAccount).ToGrainId());

    private ISalesforce Salesforce()
        => GrainFactory.GetGrain<ISalesforce>(NeuronId.For<ISalesforce>(Id.Owner, DefaultSalesforceAccount).ToGrainId());
}
