using System.Globalization;
using System.Text.Json;
using Xunit;

namespace DigitalBrain.ProductTests;

[Collection("live product")]
public sealed class LiveBehaviorRail
{
    private const string Program =
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

    private const string Feature =
        """
        Feature: account enrichment
          Scenario: enrich account from email
            Given a gmail message and salesforce account
            When the enrichment behavior runs
            Then the account description is proposed for approval
        """;

    [Fact(
        Explicit = true,
        Timeout = 900_000,
        DisplayName =
            "LIVE product: a behavior revision rides propose, BDD gate, hash-bound approval with journaled lineage")]
    public async Task BehaviorLifecycleIsObservable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = LiveProductAspire.FindRepositoryRoot();
        var behaviorId = $"live-rail-{Guid.NewGuid():N}";

        await LiveProductAspire.RunScenarioAsync(
            repository,
            ["silo", LiveProductAspire.McpResource],
            async () =>
            {
                var proposed = await LiveProductAspire.CallToolAsync(
                    repository,
                    "propose_behavior_revision",
                    JsonSerializer.Serialize(
                        new
                        {
                            behaviorId,
                            commandId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
                            programSource = Program,
                            featureText = Feature,
                            featureName = "account-enrichment",
                        }),
                    cancellationToken);
                var artifactHash = LiveProductJson.RequiredString(proposed, "proposedArtifactHash");
                Assert.False(string.IsNullOrWhiteSpace(artifactHash));

                var tested = await LiveProductAspire.CallToolAsync(
                    repository,
                    "run_behavior_tests",
                    JsonSerializer.Serialize(
                        new
                        {
                            behaviorId,
                            commandId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
                            artifactHash,
                        }),
                    cancellationToken);
                Assert.True(tested["testsPassed"]?.GetValue<bool>());

                var approved = await LiveProductAspire.CallToolAsync(
                    repository,
                    "approve_behavior_revision",
                    JsonSerializer.Serialize(
                        new
                        {
                            behaviorId,
                            commandId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
                            artifactHash,
                            approvalId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
                        }),
                    cancellationToken);
                Assert.True(approved["isApproved"]?.GetValue<bool>());

                var snapshot = await LiveProductAspire.CallToolAsync(
                    repository,
                    "read_behavior",
                    JsonSerializer.Serialize(new { behaviorId }),
                    cancellationToken);
                Assert.Equal(artifactHash, LiveProductJson.RequiredString(snapshot, "proposedArtifactHash"));
                Assert.True(snapshot["isApproved"]?.GetValue<bool>());

                var activeNeurons = await LiveProductAspire.CallToolAsync(
                    repository,
                    "list_active_neurons",
                    "{}",
                    cancellationToken);
                var behaviorNeuron = activeNeurons
                    .AsArray()
                    .Single(neuron => LiveProductJson
                        .RequiredString(neuron!, "identity")
                        .EndsWith($"/{behaviorId}", StringComparison.Ordinal));
                var grainType = LiveProductJson.RequiredString(behaviorNeuron!, "grainType");

                var journal = await LiveProductAspire.CallToolAsync(
                    repository,
                    "read_neuron_journal",
                    JsonSerializer.Serialize(
                        new
                        {
                            grainType,
                            name = behaviorId,
                            kind = "outgoing",
                            afterSequence = 0,
                        }),
                    cancellationToken);
                var lineage = LiveProductJson.RequiredArray(journal, "entries")
                    .Select(entry => LiveProductJson.RequiredString(entry!, "synapse"))
                    .ToArray();

                Assert.Contains("BehaviorRevisionProposed", lineage);
                Assert.Contains("BehaviorCompileSucceeded", lineage);
                Assert.Contains("BehaviorTestsPassed", lineage);
                Assert.Contains(lineage, fact => fact.Contains("Approv", StringComparison.Ordinal));
            },
            cancellationToken);
    }
}
