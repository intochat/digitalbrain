using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Core.V2;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Runtime;
using Orleans.Runtime;

namespace DigitalBrain.Tests.Runtime;

public sealed class EffectStateSafetyTests
{
    [Fact]
    public async Task Terminal_effect_is_replayed_without_repeating_the_provider_call()
    {
        var store = new InMemoryAggregateStore();
        await SeedEffectAsync(store);
        var handler = new CountingEffectHandler(new(EffectDisposition.Success, "applied", "provider-operation"));

        var first = await new EffectCoordinator(store, [handler])
            .ExecuteOnceAsync("aggregate", "effect", "worker-a", TimeSpan.FromMinutes(1));
        var replay = await new EffectCoordinator(store, [handler])
            .ExecuteOnceAsync("aggregate", "effect", "worker-b", TimeSpan.FromMinutes(1));

        Assert.Equal("Succeeded", first.State);
        Assert.Equal(first.TransitionId, replay.TransitionId);
        Assert.Equal("provider-operation", replay.ProviderOperationId);
        Assert.Equal(1, handler.Calls);
        Assert.Empty((await store.ReadAsync("aggregate")).Outbox);
    }

    [Fact]
    public async Task Durable_effect_lease_blocks_a_second_coordinator_while_the_provider_call_is_active()
    {
        var store = new InMemoryAggregateStore();
        await SeedEffectAsync(store);
        var handler = new BlockingEffectHandler();
        var first = new EffectCoordinator(store, [handler])
            .ExecuteOnceAsync("aggregate", "effect", "worker-a", TimeSpan.FromMinutes(1));
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var applying = Assert.Single((await store.ReadAsync("aggregate")).EffectTransitions);
        Assert.Equal("Applying", applying.State);
        Assert.NotNull(applying.LeaseExpiresAt);
        Assert.NotEqual("worker-a", applying.LeaseOwner);
        Assert.Equal(64, applying.LeaseOwner!.Length);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EffectCoordinator(store, [handler])
            .ExecuteOnceAsync("aggregate", "effect", "worker-b", TimeSpan.FromMinutes(1)));

        handler.Release.TrySetResult();
        Assert.Equal("Succeeded", (await first).State);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_before_provider_dispatch_is_terminally_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var innerStore = new InMemoryAggregateStore();
        var store = new CancelAfterClaimStore(innerStore, cancellation);
        await SeedEffectAsync(store);
        var handler = new CountingEffectHandler(new(EffectDisposition.Success));

        var result = await new EffectCoordinator(store, [handler])
            .ExecuteOnceAsync("aggregate", "effect", "worker", TimeSpan.FromMinutes(1), cancellation.Token);

        Assert.Equal("Cancelled", result.State);
        Assert.Equal("cancelled-before-dispatch", result.SafeResult);
        Assert.Equal(0, handler.Calls);
        Assert.Empty((await innerStore.ReadAsync("aggregate")).Outbox);
    }

    [Fact]
    public async Task Caller_cancellation_after_provider_dispatch_is_outcome_unknown_and_is_not_retried()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new InMemoryAggregateStore();
        await SeedEffectAsync(store);
        var handler = new CancellingEffectHandler(cancellation);
        var coordinator = new EffectCoordinator(store, [handler]);

        var result = await coordinator.ExecuteOnceAsync(
            "aggregate", "effect", "worker-a", TimeSpan.FromMinutes(1), cancellation.Token);
        var replay = await coordinator.ExecuteOnceAsync(
            "aggregate", "effect", "worker-b", TimeSpan.FromMinutes(1));

        Assert.Equal("OutcomeUnknown", result.State);
        Assert.Equal("effect-execution-cancelled", result.SafeResult);
        Assert.Equal(result.TransitionId, replay.TransitionId);
        Assert.Equal(1, handler.Calls);
        Assert.Empty((await store.ReadAsync("aggregate")).Outbox);
    }

    [Fact]
    public async Task Handler_cancelled_result_after_dispatch_is_outcome_unknown_and_is_not_retried()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new InMemoryAggregateStore();
        await SeedEffectAsync(store);
        var handler = new ReturningCancelledEffectHandler(cancellation);
        var coordinator = new EffectCoordinator(store, [handler]);

        var result = await coordinator.ExecuteOnceAsync(
            "aggregate", "effect", "worker-a", TimeSpan.FromMinutes(1), cancellation.Token);
        var replay = await coordinator.ExecuteOnceAsync(
            "aggregate", "effect", "worker-b", TimeSpan.FromMinutes(1));

        Assert.Equal("OutcomeUnknown", result.State);
        Assert.Equal("effect-execution-cancelled", result.SafeResult);
        Assert.Equal(result.TransitionId, replay.TransitionId);
        Assert.Equal(1, handler.Calls);
        Assert.Empty((await store.ReadAsync("aggregate")).Outbox);
    }

    [Fact]
    public async Task Expired_ambiguous_effect_is_verified_instead_of_executed_again()
    {
        var store = new InMemoryAggregateStore();
        await SeedEffectAsync(store);
        var clock = new TestClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        await store.AppendEffectTransitionAsync("aggregate", new EffectTransitionRecord(
            "effect", "expired-applying", "Applying", null, clock.UtcNow.AddMinutes(-2), "old-worker", clock.UtcNow.AddMinutes(-1)));
        var handler = new VerifyingEffectHandler(new(true, false, "verified"));

        var result = await new EffectCoordinator(store, [handler], clock)
            .ExecuteOnceAsync("aggregate", "effect", "worker", TimeSpan.FromMinutes(1));

        Assert.Equal("Succeeded", result.State);
        Assert.Equal(1, handler.VerificationCalls);
        Assert.Equal(0, handler.ExecutionCalls);
        Assert.Empty((await store.ReadAsync("aggregate")).Outbox);
    }

    [Fact]
    public async Task Verifier_not_found_allows_one_new_attempt_with_the_same_provider_idempotency_key()
    {
        var store = new InMemoryAggregateStore();
        await SeedEffectAsync(store, operationId: "stable-provider-key");
        var clock = new TestClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        await store.AppendEffectTransitionAsync("aggregate", new EffectTransitionRecord(
            "effect", "expired-applying", "Applying", null, clock.UtcNow.AddMinutes(-2), "old-worker", clock.UtcNow.AddMinutes(-1)));
        var handler = new VerifyingEffectHandler(new(false, true, "not-found"));

        var result = await new EffectCoordinator(store, [handler], clock)
            .ExecuteOnceAsync("aggregate", "effect", "worker", TimeSpan.FromMinutes(1));

        Assert.Equal("Succeeded", result.State);
        Assert.Equal(1, handler.VerificationCalls);
        Assert.Equal(1, handler.ExecutionCalls);
        Assert.Equal("stable-provider-key", handler.LastOperationId);
    }

    [Fact]
    public async Task Expired_effect_without_a_verifier_becomes_outcome_unknown_without_execution()
    {
        var store = new InMemoryAggregateStore();
        await SeedEffectAsync(store);
        var clock = new TestClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        await store.AppendEffectTransitionAsync("aggregate", new EffectTransitionRecord(
            "effect", "expired-applying", "Applying", null, clock.UtcNow.AddMinutes(-2), "old-worker", clock.UtcNow.AddMinutes(-1)));
        var handler = new CountingEffectHandler(new(EffectDisposition.Success));

        var result = await new EffectCoordinator(store, [handler], clock)
            .ExecuteOnceAsync("aggregate", "effect", "worker", TimeSpan.FromMinutes(1));

        Assert.Equal("OutcomeUnknown", result.State);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Failed_grain_writes_do_not_leak_uncommitted_commit_or_transition_state()
    {
        var storage = new FailingPersistentState { FailWrites = true };
        var grain = new AggregateGrain(storage);
        var payload = JsonSerializer.SerializeToElement(new { value = 1 });
        var effect = new OutboxRecord("effect", "operation", 0, "fake", payload, DateTimeOffset.UtcNow.AddMinutes(5));
        var request = new V2CommitRequest("command", 0, payload, [], [effect], DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<IOException>(() => grain.CommitAsync(request));
        var afterCommitFailure = await grain.ReadAsync();
        Assert.Equal(0, afterCommitFailure.CommitSequence);
        Assert.Empty(afterCommitFailure.Commits);
        Assert.Empty(afterCommitFailure.Inbox);
        Assert.Empty(afterCommitFailure.Outbox);

        storage.FailWrites = false;
        await grain.CommitAsync(request);
        storage.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(() => grain.AppendEffectTransitionAsync(
            new("effect", "transition", "Applying", null, DateTimeOffset.UtcNow)));
        var afterTransitionFailure = await grain.ReadAsync();
        Assert.Equal(1, afterTransitionFailure.CommitSequence);
        Assert.Single(afterTransitionFailure.Outbox);
        Assert.Empty(afterTransitionFailure.EffectTransitions);
    }

    [Fact]
    public async Task Ambiguous_write_failure_after_the_effect_transition_actually_landed_is_not_rolled_back()
    {
        var storage = new FailingPersistentState();
        var grain = new AggregateGrain(storage);
        var payload = JsonSerializer.SerializeToElement(new { value = 1 });
        var effect = new OutboxRecord("effect", "operation", 0, "fake", payload, DateTimeOffset.UtcNow.AddMinutes(5));
        await grain.CommitAsync(new V2CommitRequest("command", 0, payload, [], [effect], DateTimeOffset.UtcNow));

        // The write lands durably (the storage provider commits it) but the caller never sees the
        // acknowledgement -- a blind rollback here would silently forget a durably-committed effect
        // transition, letting EffectCoordinator's next pass re-execute an already-succeeded effect.
        storage.CommitThenThrow = true;
        await grain.AppendEffectTransitionAsync(new("effect", "transition", "Succeeded", null, DateTimeOffset.UtcNow));

        var afterAmbiguousWrite = await grain.ReadAsync();
        Assert.Single(afterAmbiguousWrite.EffectTransitions);
        Assert.Equal("transition", afterAmbiguousWrite.EffectTransitions[0].TransitionId);
    }

    [Fact]
    public async Task Ambiguous_write_failure_poisons_the_grain_and_rejects_further_calls()
    {
        var storage = new FailingPersistentState { FailWrites = true, FailReads = true };
        var grain = new AggregateGrain(storage);
        var payload = JsonSerializer.SerializeToElement(new { value = 1 });
        var effect = new OutboxRecord("effect", "operation", 0, "fake", payload, DateTimeOffset.UtcNow.AddMinutes(5));

        // The write fails AND the recovery read that would normally tell us what actually landed also
        // fails -- the durable outcome is genuinely unknown, so this activation must never be trusted again.
        await Assert.ThrowsAsync<PersistedStateWriteOutcomeUnknownException>(() => grain.CommitAsync(
            new V2CommitRequest("command", 0, payload, [], [effect], DateTimeOffset.UtcNow)));

        await Assert.ThrowsAsync<RuntimeStateIntegrityException>(() => grain.ReadAsync());
        await Assert.ThrowsAsync<RuntimeStateIntegrityException>(() => grain.AppendEffectTransitionAsync(
            new("effect", "transition", "Applying", null, DateTimeOffset.UtcNow)));
    }

    [Fact]
    public async Task Aggregate_retention_is_bounded_and_never_discards_an_active_effect_intent()
    {
        var store = new InMemoryAggregateStore();
        V2CommitRequest? mostRecentRequest = null;
        for (var index = 0; index < AggregateRetention.MaxRetainedCommits + 12; index++)
        {
            var payload = JsonSerializer.SerializeToElement(new { index });
            var snapshot = await store.ReadAsync("aggregate");
            mostRecentRequest = new(
                "command-" + index,
                snapshot.CommitSequence,
                payload,
                [],
                [new OutboxRecord("effect-" + index, "operation-" + index, 0, "fake", payload, DateTimeOffset.UtcNow.AddMinutes(5))],
                DateTimeOffset.UtcNow);
            await store.CommitAsync("aggregate", mostRecentRequest);
            await store.AppendEffectTransitionAsync("aggregate", new(
                "effect-" + index, "terminal-" + index, "Succeeded", null, DateTimeOffset.UtcNow));
        }

        var compacted = await store.ReadAsync("aggregate");
        Assert.Equal(AggregateRetention.MaxRetainedCommits, compacted.Commits.Count);
        Assert.Equal(AggregateRetention.MaxRetainedInboxRecords, compacted.Inbox.Count);
        Assert.Equal(AggregateRetention.MaxRetainedInactiveEffects, compacted.EffectTransitions.Count);
        Assert.Empty(compacted.Outbox);
        Assert.True((await store.CommitAsync("aggregate", mostRecentRequest!)).Duplicate);

        var activePayload = JsonSerializer.SerializeToElement(new { active = true });
        await store.CommitAsync("aggregate", new(
            "active-command",
            compacted.CommitSequence,
            activePayload,
            [],
            [new OutboxRecord("active-effect", "active-operation", 0, "fake", activePayload, DateTimeOffset.UtcNow.AddMinutes(5))],
            DateTimeOffset.UtcNow));
        for (var index = 0; index < AggregateRetention.MaxRetainedTransitionsPerActiveEffect + 8; index++)
            await store.AppendEffectTransitionAsync("aggregate", new(
                "active-effect",
                "active-transition-" + index,
                index % 2 == 0 ? "Applying" : "RetryScheduled",
                null,
                DateTimeOffset.UtcNow));

        var withActiveEffect = await store.ReadAsync("aggregate");
        Assert.Contains(withActiveEffect.Outbox, intent => intent.EffectId == "active-effect");
        Assert.Equal(AggregateRetention.MaxRetainedTransitionsPerActiveEffect,
            withActiveEffect.EffectTransitions.Count(transition => transition.EffectId == "active-effect"));

        await store.AppendEffectTransitionAsync("aggregate", new(
            "active-effect", "active-terminal", "Failed", null, DateTimeOffset.UtcNow));
        var afterTerminal = await store.ReadAsync("aggregate");
        Assert.DoesNotContain(afterTerminal.Outbox, intent => intent.EffectId == "active-effect");
        Assert.True(afterTerminal.EffectTransitions.Count <= AggregateRetention.MaxRetainedInactiveEffects);
    }

    private static async Task SeedEffectAsync(
        IAggregateStore store,
        string operationId = "operation")
    {
        var payload = JsonSerializer.SerializeToElement(new { value = 1 });
        var effect = new OutboxRecord(
            "effect",
            operationId,
            0,
            "fake",
            payload,
            DateTimeOffset.Parse("2030-01-01T00:00:00Z"));
        await store.CommitAsync("aggregate", new("command", 0, payload, [], [effect], DateTimeOffset.UtcNow));
    }

    private sealed class CountingEffectHandler(EffectExecutionResult result) : IEffectHandler
    {
        public string EffectType => "fake";
        public int Calls { get; private set; }

        public Task<EffectExecutionResult> ExecuteAsync(OutboxRecord intent, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class BlockingEffectHandler : IEffectHandler
    {
        public string EffectType => "fake";
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<EffectExecutionResult> ExecuteAsync(OutboxRecord intent, CancellationToken cancellationToken = default)
        {
            Calls++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(EffectDisposition.Success);
        }
    }

    private sealed class CancellingEffectHandler(CancellationTokenSource cancellation) : IEffectHandler
    {
        public string EffectType => "fake";
        public int Calls { get; private set; }

        public Task<EffectExecutionResult> ExecuteAsync(OutboxRecord intent, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellation.Cancel();
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class ReturningCancelledEffectHandler(CancellationTokenSource cancellation) : IEffectHandler
    {
        public string EffectType => "fake";
        public int Calls { get; private set; }

        public Task<EffectExecutionResult> ExecuteAsync(
            OutboxRecord intent,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellation.Cancel();
            return Task.FromResult(new EffectExecutionResult(
                EffectDisposition.Cancelled,
                "provider-cancelled"));
        }
    }

    private sealed class CancelAfterClaimStore(
        IAggregateStore inner,
        CancellationTokenSource cancellation) : IAggregateStore
    {
        public Task<V2AggregateSnapshot> ReadAsync(string aggregateId, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(aggregateId, cancellationToken);

        public Task<V2CommitResult> CommitAsync(
            string aggregateId,
            V2CommitRequest request,
            CancellationToken cancellationToken = default) =>
            inner.CommitAsync(aggregateId, request, cancellationToken);

        public Task AppendEffectTransitionAsync(
            string aggregateId,
            EffectTransitionRecord transition,
            CancellationToken cancellationToken = default) =>
            inner.AppendEffectTransitionAsync(aggregateId, transition, cancellationToken);

        public async Task<bool> TryAppendEffectTransitionAsync(
            string aggregateId,
            string effectId,
            string? expectedTransitionId,
            EffectTransitionRecord transition,
            CancellationToken cancellationToken = default)
        {
            var appended = await inner.TryAppendEffectTransitionAsync(
                aggregateId,
                effectId,
                expectedTransitionId,
                transition,
                cancellationToken);
            if (appended && string.Equals(transition.State, "Applying", StringComparison.Ordinal))
                cancellation.Cancel();
            return appended;
        }
    }

    private sealed class VerifyingEffectHandler(EffectVerificationResult verification) : IEffectHandler, IEffectVerifier
    {
        public string EffectType => "fake";
        public int ExecutionCalls { get; private set; }
        public int VerificationCalls { get; private set; }
        public string? LastOperationId { get; private set; }

        public Task<EffectExecutionResult> ExecuteAsync(OutboxRecord intent, CancellationToken cancellationToken = default)
        {
            ExecutionCalls++;
            LastOperationId = intent.OperationId;
            return Task.FromResult(new EffectExecutionResult(EffectDisposition.Success));
        }

        public Task<EffectVerificationResult> VerifyAsync(OutboxRecord intent, CancellationToken cancellationToken = default)
        {
            VerificationCalls++;
            return Task.FromResult(verification);
        }
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class FailingPersistentState : IPersistentState<AggregateGrainState>
    {
        private AggregateGrainState _committedState = new();
        private string _committedEtag = string.Empty;
        private bool _committedRecordExists;
        private int _writeAttempts;

        public AggregateGrainState State { get; set; } = new();
        public string Etag { get; set; } = string.Empty;
        public bool RecordExists { get; set; }
        public bool FailWrites { get; set; }
        public bool FailReads { get; set; }
        public bool CommitThenThrow { get; set; }

        public Task ClearStateAsync()
        {
            State = new();
            Etag = string.Empty;
            RecordExists = false;
            _committedState = State;
            _committedEtag = Etag;
            _committedRecordExists = false;
            return Task.CompletedTask;
        }

        // A real storage provider's ReadStateAsync reflects what is durably committed, not whatever the
        // grain last assigned to .State -- distinguishing the two is exactly what the reconcile-via-re-read
        // path under test depends on.
        public Task ReadStateAsync()
        {
            if (FailReads) throw new IOException("injected recovery-read failure");
            State = _committedState;
            Etag = _committedEtag;
            RecordExists = _committedRecordExists;
            return Task.CompletedTask;
        }

        public Task WriteStateAsync()
        {
            _writeAttempts++;
            if (FailWrites) throw new IOException("injected persistent-state failure");
            Etag = "etag-" + _writeAttempts;
            RecordExists = true;
            _committedState = State;
            _committedEtag = Etag;
            _committedRecordExists = true;
            if (CommitThenThrow)
            {
                CommitThenThrow = false;
                throw new IOException("injected lost write response");
            }
            return Task.CompletedTask;
        }
    }
}
