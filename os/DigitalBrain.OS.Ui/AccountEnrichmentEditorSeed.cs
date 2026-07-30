namespace DigitalBrain.Flutter.Http;

internal static class AccountEnrichmentEditorSeed
{
    public const string FeatureName = "account-enrichment";

    public const string DisplayName = "Account enrichment";

    public const string Description = "Enrich a Salesforce account from a Gmail message.";

    public static string ProgramSource { get; } =
        """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using DigitalBrain.Abstractions;
        using DigitalBrain.Behaviors;

        public sealed record EnrichTrigger(string MessageId, string AccountId, string GmailAccount) : Synapse;

        public sealed class AccountEnrichmentProgram : IBehaviorProgram<EnrichTrigger>
        {
            public ValueTask ExecuteAsync(EnrichTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
            {
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
                => ValueTask.FromResult(BehaviorInstallTestReport.Pass(1, "account-enrichment"));
        }
        """;

    public static string FeatureText { get; } =
        """
        Feature: account enrichment
          Scenario: enrich account from email
            Given a gmail message and salesforce account
            When the enrichment behavior runs
            Then the account description is proposed for approval
        """;
}
