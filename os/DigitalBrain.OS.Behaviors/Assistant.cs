using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.OS;

internal sealed class Assistant(
    [FromKeyedServices(typeof(Gemma4))] IChatClient chatClient)
    : Agent(chatClient), IAssistant
{
    public const string EnrichAccountFromEmail = "enrich_account_from_email";
    public const string DefaultGmailAccount = "gmail";
    public const string DefaultSalesforceAccount = "salesforce";

    protected override string? Instructions =>
        """
        You are DigitalBrain, the AI assistant inside the DigitalBrain personal agent system.
        Be precise about your identity and never invent tools, integrations, or completed actions.

        You can provide general conversational help using the current chat context. In this
        deployment you have one external action: enrich_account_from_email. Given an exact Gmail
        message ID and a 15- or 18-character Salesforce Account ID, it reads that specific Gmail
        message and creates a durable proposal to replace that Account's Description. The proposal
        is not written to Salesforce until the owner separately gives explicit approval.

        You cannot search or list Gmail, send email, directly update Salesforce, approve a proposal,
        or create accounts, contacts, or leads. Ask for both required IDs when either is missing.
        Do not call the tool merely to describe your capabilities. Never describe a proposal as a
        completed update. If authentication or a provider is unavailable, report that limitation.
        """;

    protected override IReadOnlyList<CapabilityTool> Tools =>
    [
        Capability(
            EnrichAccountFromEmail,
            "Given an exact Gmail message ID and a 15- or 18-character Salesforce Account ID, "
            + "read only that message and create an approval-gated Account Description proposal. "
            + "This cannot search Gmail or directly write Salesforce.",
            ([Description("Salesforce account id, 15 or 18 characters")] string accountId,
             [Description("Gmail message id to read")] string messageId)
                => ProposeEnrichmentAsync(accountId, messageId)),
    ];

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
            throw new ArgumentException(
                "A Salesforce Account ID must be a 15- or 18-character alphanumeric value.",
                nameof(accountId));
        }
    }

    private IGmail Gmail()
        => GrainFactory.GetGrain<IGmail>(
            NeuronId.For<IGmail>(Id.Owner, DefaultGmailAccount).ToGrainId());

    private ISalesforce Salesforce()
        => GrainFactory.GetGrain<ISalesforce>(
            NeuronId.For<ISalesforce>(Id.Owner, DefaultSalesforceAccount).ToGrainId());
}
