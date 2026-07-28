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

    protected override IReadOnlyList<CapabilityTool> Tools =>
    [
        Capability(
            EnrichAccountFromEmail,
            "Read a Gmail message and propose it as the description of a Salesforce account. "
            + "The proposal always waits for the owner's approval before anything is written.",
            ([Description("Salesforce account id, 15 or 18 characters")] string accountId,
             [Description("Gmail message id to read")] string messageId)
                => ProposeEnrichmentAsync(accountId, messageId)),
    ];

    private async Task<string> ProposeEnrichmentAsync(string accountId, string messageId)
    {
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

    private IGmail Gmail()
        => GrainFactory.GetGrain<IGmail>(
            NeuronId.For<IGmail>(Id.Owner, DefaultGmailAccount).ToGrainId());

    private ISalesforce Salesforce()
        => GrainFactory.GetGrain<ISalesforce>(
            NeuronId.For<ISalesforce>(Id.Owner, DefaultSalesforceAccount).ToGrainId());
}
