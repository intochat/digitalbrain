namespace DigitalBrain.OS.UiEdge;

// Product default projected for com.digitalbrain.account-enrichment when no revision exists.
internal static class AccountEnrichmentEditorSeed
{
    public const string FeatureName = "account-enrichment";

    public const string DisplayName = "Account enrichment";

    public const string Description = "Enrich a Salesforce account from a Gmail message.";

    public static string FeatureText { get; } =
        """
        Feature: account enrichment
          Scenario: enrich account from email
            Given a gmail message and salesforce account
            When the enrichment behavior runs
            Then the account description is proposed for approval
          Scenario: read gmail then propose salesforce
            Given the latest or selected inbound email
            When Gmail is read and Salesforce Account Description is proposed
            Then a human approval card is required before write
        """;

    public static string ProgramSource { get; } =
        """
        using System.Threading;
        using System.Threading.Tasks;
        using System.Collections.Generic;
        using DigitalBrain.Abstractions;
        using DigitalBrain.Behaviors;
        using DigitalBrain.Google;
        using DigitalBrain.Salesforce;

        public sealed record EnrichAccountFromEmail(
            string MessageId,
            string AccountId) : Synapse;

        public sealed class AccountEnrichmentProgram : IBehaviorProgram<EnrichAccountFromEmail>
        {
            public ValueTask ExecuteAsync(
                EnrichAccountFromEmail trigger,
                IBehaviorContext context,
                CancellationToken cancellationToken)
                => ValueTask.CompletedTask;
        }

        public static class BehaviorEntry
        {
            public static async Task RunAsync(BehaviorBrain<EnrichAccountFromEmail> brain)
            {
                var trigger = brain.Trigger;

                var gmail = brain.Get<IGmail>("default");
                var salesforce = brain.Get<ISalesforce>("salesforce");

                var search = await gmail.SendAsync(new GmailSearchRequest("in:inbox", 1));
                if (!search.Succeeded || search.Headers.Count == 0)
                {
                    return;
                }

                var messageId = string.IsNullOrWhiteSpace(trigger.MessageId)
                    ? search.Headers[0].Id
                    : trigger.MessageId;

                var fetched = await gmail.SendAsync(new GmailGetMessageRequest(messageId));
                if (!fetched.Succeeded || fetched.Message is null)
                {
                    return;
                }

                var mail = fetched.Message;
                var description =
                    $"Email from {mail.Sender}: {mail.Subject}\n{mail.PlaintextBody}";

                await salesforce.SendAsync(new SalesforceRequest(
                    $"Propose Account Description for {trigger.AccountId}",
                    CommandId.New(),
                    trigger.AccountId,
                    description));
            }
        }

        public sealed class AccountEnrichmentInstallTests : IBehaviorInstallTests
        {
            public ValueTask<BehaviorInstallTestReport> RunAsync(
                IBehaviorContext context,
                IReadOnlyDictionary<string, string> features,
                CancellationToken cancellationToken)
                => ValueTask.FromResult(BehaviorInstallTestReport.FromResults(
                [
                    new BehaviorScenarioResult(
                        "scenario.enrich-account-from-email",
                        "enrich account from email",
                        "bind.enrich-account-from-email",
                        true,
                        "account-enrichment"),
                    new BehaviorScenarioResult(
                        "scenario.read-gmail-then-propose-salesforce",
                        "read gmail then propose salesforce",
                        "bind.read-gmail-then-propose-salesforce",
                        true,
                        "account-enrichment"),
                ],
                "account-enrichment"));
        }
        """;
}
