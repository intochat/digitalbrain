using System.Globalization;
using System.Text.Json;
using Xunit;

namespace DigitalBrain.ProductTests;

[Collection("live product")]
public sealed class LiveBehaviorStudio
{
    private const string Program =
        """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using DigitalBrain.Abstractions;
        using DigitalBrain.Behaviors;

        public sealed record StudioTrigger(string Label) : Synapse;

        public sealed class StudioProgram : IBehaviorProgram<StudioTrigger>
        {
            public ValueTask ExecuteAsync(StudioTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
            {
                context.SetState("outcome", "studio:" + trigger.Label);
                return ValueTask.CompletedTask;
            }
        }

        public sealed class StudioInstallTests : IBehaviorInstallTests
        {
            public ValueTask<BehaviorInstallTestReport> RunAsync(
                IBehaviorContext context,
                IReadOnlyDictionary<string, string> features,
                CancellationToken cancellationToken)
                => ValueTask.FromResult(BehaviorInstallTestReport.FromResults(
                [
                    new BehaviorScenarioResult(
                        "scenario.studio-install",
                        "studio install passes",
                        "bind.studio-install",
                        true,
                        "studio"),
                ],
                "studio"));
        }
        """;

    private const string Feature =
        """
        Feature: studio sample
          Scenario: studio install passes
            Then the install gate passes
        """;

    [Fact(
        Explicit = true,
        Timeout = 900_000,
        DisplayName =
            "LIVE product: behavior studio rail proposes, tests, and approves a revision with retained artifact hash")]
    public async Task BehaviorStudioRailIsObservable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = LiveProductAspire.FindRepositoryRoot();
        var behaviorId = $"live-studio-{Guid.NewGuid():N}";

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
                            featureName = "studio-sample",
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
            },
            cancellationToken);
    }
}
