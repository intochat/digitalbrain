using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Client;
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

    [SuppressMessage(
        "Usage",
        "CA1849:Call async methods when in an async method",
        Justification = "DigitalBrainClient.Connect is the in-silo factory; ConnectAsync is the behavior-worker surface.")]
    private async Task<string> ProposeEnrichmentAsync(string accountId, string messageId)
    {
        ValidateAccountId(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var brain = DigitalBrainClient.Connect(GrainFactory, Id.Owner.Value);
        var mail = await brain.Get<IGmail>(DefaultGmailAccount)
            .SendAsync(new GmailRequest($"Read Gmail message {messageId}"), CancellationToken.None);
        if (!mail.Succeeded)
        {
            throw new InvalidOperationException(mail.Error ?? "Gmail intent failed.");
        }

        GmailMessage? message = null;
        for (var index = 0; index < mail.Messages.Count; index++)
        {
            if (string.Equals(mail.Messages[index].Id, messageId, StringComparison.Ordinal))
            {
                message = mail.Messages[index];
                break;
            }
        }

        message ??= mail.Messages.Count > 0
            ? mail.Messages[0]
            : throw new InvalidOperationException($"Gmail returned no message for '{messageId}'.");
        var description = $"Email from {message.Sender}: {message.Subject}\n{message.PlaintextBody}";
        var proposed = await brain.Get<ISalesforce>(DefaultSalesforceAccount)
            .SendAsync(
                new SalesforceRequest(
                    $"Propose Account Description for {accountId}",
                    CommandId.New(),
                    accountId,
                    description),
                CancellationToken.None);
        if (!proposed.Succeeded || proposed.Mutation is null)
        {
            throw new InvalidOperationException(proposed.Error ?? "Salesforce propose failed.");
        }

        return $"Proposed a description for account {accountId} from message {messageId}. "
            + $"It is {proposed.Mutation.State} and will not be written until you approve it.";
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

}
