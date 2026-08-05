namespace DigitalBrain.OS.UiEdge;

// Default Account Enrichment projected when com.digitalbrain.account-enrichment
// has no authored revision. Mirrors samples/DigitalBrain.AccountEnrichment.
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

    // Visualized in Behavior Studio Source; rail-compilable stub + Gmail/SF story.
    public static string ProgramSource { get; } =
        """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using DigitalBrain.Abstractions;
        using DigitalBrain.Behaviors;

        // Product demo: Account enrichment (Gmail → Salesforce).
        // Process-neuron reference: samples/DigitalBrain.AccountEnrichment
        //   EnrichAccountFromEmail
        //     → IGmail GmailRequest / GmailSearchRequest (read message)
        //     → description = "Email from {From}: {Subject}\n{Body}"
        //     → ISalesforce SalesforceRequest (propose Account Description)
        //     → human SalesforceMutationApproval
        //     → AccountEnriched
        //
        // This single-file program is what Behavior Studio shows and what the
        // rail can compile. Live Gmail/SF SendAsync needs capability grants.

        public sealed record EnrichAccountFromEmail(
            string MessageId,
            string AccountId,
            string GmailAccount) : Synapse;

        public sealed class AccountEnrichmentProgram : IBehaviorProgram<EnrichAccountFromEmail>
        {
            public ValueTask ExecuteAsync(
                EnrichAccountFromEmail trigger,
                IBehaviorContext context,
                CancellationToken cancellationToken)
            {
                // Intended live path (when grants + Gmail/Salesforce connections are armed):
                //   var gmail = context.Get<IGmail>(trigger.GmailAccount);
                //   var mail = await gmail.SendAsync(new GmailRequest(
                //       $"Read Gmail message {trigger.MessageId}",
                //       context.DeterministicCommandId("gmail-read")));
                //   var description = $"Email from {from}: {subject}\n{body}";
                //   var sf = context.Get<ISalesforce>("salesforce");
                //   await sf.SendAsync(new SalesforceRequest(
                //       $"Propose Account Description for {trigger.AccountId}",
                //       context.DeterministicCommandId("sf-propose"),
                //       trigger.AccountId,
                //       description));

                context.SetState(
                    "outcome",
                    $"enriched:{trigger.AccountId}:{trigger.MessageId}:{trigger.GmailAccount}");
                return ValueTask.CompletedTask;
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
