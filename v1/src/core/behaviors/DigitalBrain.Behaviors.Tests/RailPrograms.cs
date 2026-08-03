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
                    => ValueTask.FromResult(BehaviorInstallTestReport.FromResults(
                    [
                        new BehaviorScenarioResult(
                            "scenario.install-gate-passes",
                            "install gate passes",
                            "bind.install-gate-passes",
                            true,
                            "green"),
                    ],
                    "green"));
            }
            """;

    public static string UnionGreenProgram()
        => """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Behaviors;

            public sealed record ManualResearchRequest(string Prompt) : Synapse;
            public sealed record GmailMessageReceived(string MessageId) : Synapse;
            public union ResearchCompanyRequest(ManualResearchRequest, GmailMessageReceived);

            public sealed class SampleProgram : IBehaviorProgram<ManualResearchRequest>
            {
                public ValueTask ExecuteAsync(ManualResearchRequest trigger, IBehaviorContext context, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }

            public sealed class SampleInstallTests : IBehaviorInstallTests
            {
                public ValueTask<BehaviorInstallTestReport> RunAsync(
                    IBehaviorContext context,
                    IReadOnlyDictionary<string, string> features,
                    CancellationToken cancellationToken)
                    => ValueTask.FromResult(BehaviorInstallTestReport.FromResults(
                    [
                        new BehaviorScenarioResult(
                            "scenario.install-gate-passes",
                            "install gate passes",
                            "bind.install-gate-passes",
                            true,
                            "green"),
                    ],
                    "green"));
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
                    => ValueTask.FromResult(BehaviorInstallTestReport.FromResults(
                    [
                        new BehaviorScenarioResult(
                            "scenario.install-gate-fails",
                            "install gate fails",
                            "bind.install-gate-fails",
                            false,
                            "scenario red"),
                    ],
                    "scenario red"));
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
                    => ValueTask.FromResult(BehaviorInstallTestReport.FromResults(
                    [
                        new BehaviorScenarioResult(
                            "scenario.enrich-account-from-email",
                            "enrich account from email",
                            "bind.enrich-account-from-email",
                            true,
                            "account-enrichment"),
                    ],
                    "account-enrichment"));
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

    public static string SingleFileSdkProgram()
        => """
            using System.Threading.Tasks;
            using DigitalBrain.Abstractions;
            using DigitalBrain.Client;
            using DigitalBrain.Behaviors;

            public sealed record ResearchCompanyRequest(string Prompt) : Synapse;
            public sealed record GmailResponse(string Status) : Synapse;
            public sealed record GmailRequest(string Prompt) : RequestSynapse<GmailResponse>;
            public interface IGmail : INeuron;

            public static class BehaviorEntry
            {
                public static async Task RunAsync()
                {
                    await using var brain =
                        await DigitalBrainClient.ConnectAsync<ResearchCompanyRequest>();

                    var request = brain.Trigger;
                    var gmail = brain.Get<IGmail>();
                    var result = await gmail.SendAsync(new GmailRequest(request.Prompt));
                }
            }
            """;
}
