using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Features;
using DigitalBrain.Kernel.Runtime;

namespace DigitalBrain.OrleansTests.Features;

public sealed class FeatureTransitionLimitTests
{
    private static readonly ReleaseDigest Release = new(new string('a', 64));
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Hub_accepts_one_hundred_installations_and_rejects_the_next()
    {
        var state = FeatureHubState.Empty;

        for (var index = 0; index < FeatureLimits.InstallationsPerOwner; index++)
        {
            state = FeatureHubTransitions.Register(
                state,
                new FeatureInstallationRegistration(new($"installation-{index}"), Release, ["email.received"]));
        }

        Assert.Equal(100, state.Installations.Length);
        Assert.Throws<FeatureLimitExceededException>(() => FeatureHubTransitions.Register(
            state,
            new FeatureInstallationRegistration(new("installation-overflow"), Release, ["email.received"])));
    }

    [Fact]
    public void Installation_accepts_one_thousand_inputs_then_pauses_on_overflow()
    {
        var state = FeatureInstallationState.Create(Release);

        for (var index = 0; index < FeatureLimits.InboxEntries; index++)
        {
            var appended = FeatureInstallationTransitions.Append(state, Input(index), Now);
            Assert.Equal(FeatureAppendStatus.Accepted, appended.Status);
            state = appended.State;
        }

        var overflow = FeatureInstallationTransitions.Append(state, Input(FeatureLimits.InboxEntries), Now);

        Assert.Equal(FeatureAppendStatus.Full, overflow.Status);
        Assert.True(overflow.State.Paused);
        Assert.Equal(1_000, overflow.State.Inbox.Length);
    }

    [Fact]
    public void Commit_accepts_sixty_four_kibibytes_of_state_and_rejects_one_more_byte()
    {
        var exact = ClaimedState();
        var committed = FeatureInstallationTransitions.Commit(
            exact.State,
            Commit(exact.Claim.Fence, JsonPayload(FeatureLimits.StateUtf8Bytes)),
            Now.AddSeconds(1));

        Assert.Equal(FeatureLimits.StateUtf8Bytes, System.Text.Encoding.UTF8.GetByteCount(committed.State.StateJson));

        var over = ClaimedState(inputIndex: 1);
        Assert.Throws<FeatureLimitExceededException>(() => FeatureInstallationTransitions.Commit(
            over.State,
            Commit(over.Claim.Fence, JsonPayload(FeatureLimits.StateUtf8Bytes + 1)),
            Now.AddSeconds(1)));
    }

    [Fact]
    public void Commit_accepts_thirty_two_intents_and_rejects_the_next()
    {
        var exact = ClaimedState();
        var intents = Enumerable.Range(0, FeatureLimits.IntentsPerRun)
            .Select(index => new FeatureIntent($"intent-{index}", FeatureIntentKind.Event, "{}"))
            .ToArray();

        var committed = FeatureInstallationTransitions.Commit(
            exact.State,
            Commit(exact.Claim.Fence, "{}", intents),
            Now.AddSeconds(1));

        Assert.Equal(32, committed.State.Intents.Length);

        var over = ClaimedState(inputIndex: 1);
        Assert.Throws<FeatureLimitExceededException>(() => FeatureInstallationTransitions.Commit(
            over.State,
            Commit(
                over.Claim.Fence,
                "{}",
                Enumerable.Range(0, FeatureLimits.IntentsPerRun + 1)
                    .Select(index => new FeatureIntent($"intent-{index}", FeatureIntentKind.Event, "{}"))
                    .ToArray()),
            Now.AddSeconds(1)));
    }

    [Fact]
    public void Claim_accepts_a_sixty_second_lease_and_rejects_a_longer_lease()
    {
        var state = FeatureInstallationTransitions.Append(
            FeatureInstallationState.Create(Release),
            Input(0),
            Now).State;

        var claimed = FeatureInstallationTransitions.Claim(state, "host-1", Now, TimeSpan.FromSeconds(60));

        Assert.NotNull(claimed.Claim);
        Assert.Equal(Now.AddSeconds(60), claimed.Claim.LeaseExpiresAt);
        Assert.Throws<FeatureLimitExceededException>(() =>
            FeatureInstallationTransitions.Claim(state, "host-1", Now, TimeSpan.FromSeconds(60).Add(TimeSpan.FromTicks(1))));
    }

    [Fact]
    public void Commit_accepts_twenty_reads_and_four_model_calls()
    {
        var exact = ClaimedState();
        var committed = FeatureInstallationTransitions.Commit(
            exact.State,
            Commit(
                exact.Claim.Fence,
                "{}",
                usage: new FeatureResourceUsage(FeatureLimits.ReadsPerRun, FeatureLimits.ModelCallsPerRun)),
            Now.AddSeconds(1));

        Assert.Single(committed.State.Completions);

        var excessiveReads = ClaimedState(inputIndex: 1);
        Assert.Throws<FeatureLimitExceededException>(() => FeatureInstallationTransitions.Commit(
            excessiveReads.State,
            Commit(
                excessiveReads.Claim.Fence,
                "{}",
                usage: new FeatureResourceUsage(FeatureLimits.ReadsPerRun + 1, 0)),
            Now.AddSeconds(1)));

        var excessiveModels = ClaimedState(inputIndex: 2);
        Assert.Throws<FeatureLimitExceededException>(() => FeatureInstallationTransitions.Commit(
            excessiveModels.State,
            Commit(
                excessiveModels.Claim.Fence,
                "{}",
                usage: new FeatureResourceUsage(0, FeatureLimits.ModelCallsPerRun + 1)),
            Now.AddSeconds(1)));
    }

    [Fact]
    public void Fifth_failed_attempt_parks_the_input_and_pauses_the_installation()
    {
        var state = FeatureInstallationTransitions.Append(
            FeatureInstallationState.Create(Release),
            Input(0),
            Now).State;

        for (var attempt = 1; attempt <= FeatureLimits.AttemptsPerInput; attempt++)
        {
            var at = Now.AddSeconds(attempt);
            var claimed = FeatureInstallationTransitions.Claim(state, "host-1", at, TimeSpan.FromSeconds(60));
            Assert.NotNull(claimed.Claim);
            state = FeatureInstallationTransitions.Fail(
                claimed.State,
                claimed.Claim.Fence,
                at,
                at,
                "safe failure");
        }

        Assert.True(state.Paused);
        Assert.True(Assert.Single(state.Inbox).Parked);
        Assert.Equal(5, state.Inbox[0].Attempts);
    }

    [Fact]
    public void Pending_fan_out_is_never_evicted_when_the_durable_ledger_is_full()
    {
        var registration = new FeatureInstallationRegistration(new("installation-1"), Release, ["email.received"]);
        var state = FeatureHubTransitions.Register(FeatureHubState.Empty, registration);
        for (var index = 0; index < FeatureLimits.FanOutBatches; index++)
            state = FeatureHubTransitions.BeginFanOut(state, Input(index));

        Assert.Throws<FeatureLimitExceededException>(() =>
            FeatureHubTransitions.BeginFanOut(state, Input(FeatureLimits.FanOutBatches)));
        Assert.Equal(FeatureLimits.FanOutBatches, state.FanOuts.Length);
        Assert.All(state.FanOuts, batch => Assert.Contains(batch.Deliveries, delivery => !delivery.Delivered));
    }

    [Fact]
    public void Commit_prunes_oldest_applied_intents_but_never_pending_intents_at_the_aggregate_limit()
    {
        var applied = Enumerable.Range(0, FeatureLimits.IntentLedgerEntries)
            .Select(index => new PersistedFeatureIntent(
                $"applied-{index}",
                FeatureIntentKind.Event,
                "{}",
                Now.AddSeconds(index)))
            .ToArray();
        var claimed = ClaimedState();
        var state = claimed.State with { Intents = applied };

        var committed = FeatureInstallationTransitions.Commit(
            state,
            Commit(
                claimed.Claim.Fence,
                "{}",
                [
                    new FeatureIntent("new-pending", FeatureIntentKind.Event, "{}"),
                ]),
            Now.AddSeconds(1));

        Assert.Equal(FeatureLimits.IntentLedgerEntries, committed.State.Intents.Length);
        Assert.DoesNotContain(committed.State.Intents, intent => intent.OperationKey == "applied-0");
        Assert.Contains(committed.State.Intents, intent => intent.AppliedAt is null);

        var pending = applied.Select(intent => intent with { AppliedAt = null }).ToArray();
        var otherClaim = ClaimedState(inputIndex: 1);
        var pendingState = otherClaim.State with { Intents = pending };
        Assert.Throws<FeatureLimitExceededException>(() => FeatureInstallationTransitions.Commit(
            pendingState,
            Commit(
                otherClaim.Claim.Fence,
                "{}",
                [
                    new FeatureIntent("overflow", FeatureIntentKind.Event, "{}"),
                ]),
            Now.AddSeconds(1)));
    }

    [Fact]
    public void Commit_and_resolution_reject_operation_keys_above_the_per_record_bound()
    {
        var claimed = ClaimedState();
        Assert.Throws<ArgumentException>(() => FeatureInstallationTransitions.Commit(
            claimed.State,
            Commit(
                claimed.Claim.Fence,
                "{}",
                [new FeatureIntent(new string('x', 1025), FeatureIntentKind.ExternalEffect, "{}")]),
            Now.AddSeconds(1)));

        var committed = FeatureInstallationTransitions.Commit(
            claimed.State,
            Commit(
                claimed.Claim.Fence,
                "{}",
                [new FeatureIntent("effect", FeatureIntentKind.ExternalEffect, "{}")]),
            Now.AddSeconds(1));

        Assert.Throws<ArgumentException>(() => FeatureInstallationTransitions.ResolveIntent(
            committed.State,
            Resolution(new string('x', 1025), Now.AddSeconds(2))));
    }

    [Fact]
    public void Legacy_resolution_overflow_retains_the_incoming_resolution_and_compacts_deterministically()
    {
        var claimed = ClaimedState();
        var committed = FeatureInstallationTransitions.Commit(
            claimed.State,
            Commit(
                claimed.Claim.Fence,
                "{}",
                [new FeatureIntent("incoming", FeatureIntentKind.ExternalEffect, "{}")]),
            Now.AddSeconds(1));
        var incoming = Assert.Single(committed.State.Intents);
        var legacy = Enumerable.Range(0, FeatureLimits.EffectResolutionsPerRun)
            .Select(index => Resolution($"legacy-{index:D2}", Now.AddMinutes(index)))
            .ToArray();
        var seeded = committed.State with
        {
            Completions =
            [
                committed.Completion with
                {
                    EffectCount = 0,
                    EffectResolutions = legacy
                }
            ]
        };
        var resolution = Resolution(incoming.OperationKey, Now.AddHours(1));

        var first = FeatureInstallationTransitions.ResolveIntent(seeded, resolution);
        var second = FeatureInstallationTransitions.ResolveIntent(seeded, resolution);
        var history = Assert.IsType<FeatureEffectResolution[]>(Assert.Single(first.Completions).EffectResolutions);

        Assert.Equal(FeatureLimits.EffectResolutionsPerRun, history.Length);
        Assert.Contains(resolution, history);
        Assert.DoesNotContain(history, item => item.OperationKey == "legacy-00");
        Assert.Equal(history.OrderBy(item => item.OperationKey, StringComparer.Ordinal), history);
        Assert.Equal(history, Assert.Single(second.Completions).EffectResolutions);
    }

    [Fact]
    public void Legacy_resolution_history_compacts_deterministically_under_the_utf8_byte_bound()
    {
        var claimed = ClaimedState();
        var committed = FeatureInstallationTransitions.Commit(
            claimed.State,
            Commit(
                claimed.Claim.Fence,
                "{}",
                [new FeatureIntent("incoming", FeatureIntentKind.ExternalEffect, "{}")]),
            Now.AddSeconds(1));
        var incoming = Assert.Single(committed.State.Intents);
        var legacy = Enumerable.Range(0, FeatureLimits.EffectResolutionsPerRun)
            .Select(index => Resolution($"legacy-{index:D2}-" + new string('\u4e00', 1200), Now.AddMinutes(index)))
            .ToArray();
        var seeded = committed.State with
        {
            Completions =
            [
                committed.Completion with
                {
                    EffectCount = 0,
                    EffectResolutions = legacy
                }
            ]
        };
        var resolution = Resolution(incoming.OperationKey, Now.AddHours(1));

        var resolved = FeatureInstallationTransitions.ResolveIntent(seeded, resolution);
        var history = Assert.IsType<FeatureEffectResolution[]>(Assert.Single(resolved.Completions).EffectResolutions);

        Assert.Contains(resolution, history);
        Assert.True(ResolutionHistoryBytes(history) <= FeatureLimits.EffectResolutionHistoryUtf8Bytes);
        Assert.Equal(history.OrderBy(item => item.OperationKey, StringComparer.Ordinal), history);
    }

    [Fact]
    public void Maximum_metadata_for_thirty_two_effects_survives_pruning_with_stable_projection_and_exact_replay()
    {
        var claimed = ClaimedState();
        var committed = FeatureInstallationTransitions.Commit(
            claimed.State,
            Commit(
                claimed.Claim.Fence,
                "{}",
                Enumerable.Range(0, FeatureLimits.EffectResolutionsPerRun)
                    .Select(index => new FeatureIntent(
                        MaximumLogicalOperationKey(index),
                        FeatureIntentKind.ExternalEffect,
                        "{}"))
                    .ToArray()),
            Now.AddSeconds(1));
        var resolutions = committed.State.Intents.Select((intent, index) => new FeatureEffectResolution(
            intent.OperationKey,
            new string((char)('\u4e00' + index), 256),
            new string('a', 64),
            index == 0 ? InoEffectTerminalKind.Failed : InoEffectTerminalKind.Approved,
            Now.AddMinutes(index + 1),
            new string('\u4f00', 512))).ToArray();
        var resolved = resolutions.Aggregate(
            committed.State,
            FeatureInstallationTransitions.ResolveIntent);
        var before = Assert.Single(FeatureRunProjection.Project(resolved));
        var pruned = resolved with { Intents = [] };
        var after = Assert.Single(FeatureRunProjection.Project(pruned));
        var replay = FeatureInstallationTransitions.ResolveIntent(pruned, resolutions[0]);
        var completion = Assert.Single(pruned.Completions);
        var history = Assert.IsType<FeatureEffectResolution[]>(completion.EffectResolutions);

        Assert.Equal(FeatureLimits.EffectResolutionsPerRun, completion.EffectCount);
        Assert.Equal(FeatureLimits.EffectResolutionsPerRun, history.Length);
        Assert.All(history, item => Assert.Equal(1024, System.Text.Encoding.UTF8.GetByteCount(item.OperationKey)));
        Assert.All(history, item => Assert.Equal(768, System.Text.Encoding.UTF8.GetByteCount(item.DecisionId)));
        Assert.All(history, item => Assert.Equal(1536, System.Text.Encoding.UTF8.GetByteCount(item.SafeResult)));
        Assert.Equal(108_544, ResolutionHistoryBytes(history));
        Assert.Equal(FeatureRunStatus.Failed, before.Status);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(resolutions[^1].ResolvedAt, before.CompletedAt);
        Assert.Equal(before.CompletedAt, after.CompletedAt);
        Assert.Equal(before.SafeFailure, after.SafeFailure);
        Assert.Same(pruned, replay);
    }

    private static FeatureEffectResolution Resolution(string operationKey, DateTimeOffset resolvedAt) => new(
        operationKey,
        "decision-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(operationKey))),
        new string('a', 64),
        InoEffectTerminalKind.Approved,
        resolvedAt,
        "The provider update succeeded.");

    private static int ResolutionHistoryBytes(IEnumerable<FeatureEffectResolution> resolutions) => resolutions.Sum(item =>
        System.Text.Encoding.UTF8.GetByteCount(item.OperationKey) +
        System.Text.Encoding.UTF8.GetByteCount(item.DecisionId) +
        System.Text.Encoding.UTF8.GetByteCount(item.ActorScope) +
        System.Text.Encoding.UTF8.GetByteCount(item.SafeResult));

    private static string MaximumLogicalOperationKey(int index) =>
        $"effect-{index:D2}-" + new string('x', 991);

    private static (FeatureInstallationState State, FeatureRunClaim Claim) ClaimedState(int inputIndex = 0)
    {
        var appended = FeatureInstallationTransitions.Append(
            FeatureInstallationState.Create(Release),
            Input(inputIndex),
            Now);
        var claimed = FeatureInstallationTransitions.Claim(
            appended.State,
            "host-1",
            Now,
            TimeSpan.FromSeconds(60));
        return (claimed.State, Assert.IsType<FeatureRunClaim>(claimed.Claim));
    }

    private static FeatureInput Input(int index) => new(
        $"input-{index}",
        "email.received",
        "{}",
        Now,
        $"correlation-{index}",
        $"trace-{index}");

    private static FeatureRunCommit Commit(
        FeatureLeaseFence fence,
        string stateJson,
        IReadOnlyList<FeatureIntent>? intents = null,
        FeatureResourceUsage? usage = null) => new(
            fence,
            stateJson,
            intents ?? [],
            usage ?? new FeatureResourceUsage(0, 0),
            "{}");

    private static string JsonPayload(int utf8Bytes) => $"{{\"v\":\"{new string('x', utf8Bytes - 8)}\"}}";
}
