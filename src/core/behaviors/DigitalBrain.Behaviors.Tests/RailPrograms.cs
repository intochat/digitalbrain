namespace DigitalBrain.Behaviors.Tests;

internal static class RailPrograms
{
    public const string GreenFeature =
        """
        Feature: sample behavior
          Scenario: install gate passes
            Then the install gate passes
        """;

    public const string RedFeature =
        """
        Feature: sample behavior
          Scenario: install gate fails
            Then the install gate fails
        """;

    public static string GreenProgram(string outcome = "v1-green")
        => $$"""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;

            public sealed record SampleTrigger(string Label) : Synapse;

            public sealed class SampleProgram : IBehaviorProgram<SampleTrigger>
            {
                public ValueTask ExecuteAsync(SampleTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
                {
                    context.SetState("outcome", "{{outcome}}:" + trigger.Label);
                    return ValueTask.CompletedTask;
                }
            }

            public sealed class SampleInstallTests : IBehaviorInstallTests
            {
                public ValueTask<BehaviorInstallTestReport> RunAsync(
                    IBehaviorContext context,
                    IReadOnlyDictionary<string, string> features,
                    CancellationToken cancellationToken)
                    => ValueTask.FromResult(BehaviorInstallTestReport.Pass(1, "green"));
            }
            """;

    public static string RedProgram()
        => """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;

            public sealed record SampleTrigger(string Label) : Synapse;

            public sealed class SampleProgram : IBehaviorProgram<SampleTrigger>
            {
                public ValueTask ExecuteAsync(SampleTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }

            public sealed class SampleInstallTests : IBehaviorInstallTests
            {
                public ValueTask<BehaviorInstallTestReport> RunAsync(
                    IBehaviorContext context,
                    IReadOnlyDictionary<string, string> features,
                    CancellationToken cancellationToken)
                    => ValueTask.FromResult(BehaviorInstallTestReport.Fail("scenario red"));
            }
            """;

    public static string BrokenProgram()
        => """
            public sealed class Broken
            {
                this does not compile
            }
            """;

    public static string AccountEnrichmentProgram()
        => """
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

    public const string AccountEnrichmentFeature =
        """
        Feature: account enrichment
          Scenario: enrich account from email
            Given a gmail message and salesforce account
            When the enrichment behavior runs
            Then the account description is proposed for approval
        """;
}
