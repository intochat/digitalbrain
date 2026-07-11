using System.Text.Json;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Core.V2;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Runtime;
using Orleans.Runtime;
using RuntimeRequestContext = DigitalBrain.Core.Runtime.RequestContext;

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
    public async Task Retry_is_durable_pending_work_and_cancelled_is_a_terminal_outcome()
    {
        var context = Context();
        var retryApplication = new ApplicationService();
        var retryOperation = await retryApplication.SubmitAsync(context, Command(context, "retry"));
        var retryHandler = new SequenceCommandHandler(
            new(WorkflowState.RetryScheduled, "retry"),
            CommandExecutionResult.Success());
        var retryDispatcher = new CommandDispatcher(retryApplication, [retryHandler]);

        Assert.True(await retryDispatcher.DispatchAsync(retryOperation.OperationId));
        Assert.Equal(WorkflowState.RetryScheduled,
            (await retryApplication.GetOperationAsync(context, retryOperation.OperationId))!.State);
        Assert.Equal([retryOperation.OperationId], retryApplication.GetPendingOperationIds());
        Assert.True(await retryDispatcher.DispatchAsync(retryOperation.OperationId));
        Assert.Equal(WorkflowState.Succeeded,
            (await retryApplication.GetOperationAsync(context, retryOperation.OperationId))!.State);
        Assert.Empty(retryApplication.GetPendingOperationIds());
        Assert.Equal(2, retryHandler.Calls);

        var cancelApplication = new ApplicationService();
        var cancelOperation = await cancelApplication.SubmitAsync(context with { IdempotencyKey = "cancel" },
            Command(context with { IdempotencyKey = "cancel" }, "cancel"));
        var cancelDispatcher = new CommandDispatcher(cancelApplication,
            [new SequenceCommandHandler(new CommandExecutionResult(WorkflowState.Cancelled, "cancelled"))]);
        Assert.True(await cancelDispatcher.DispatchAsync(cancelOperation.OperationId));
        Assert.Equal(WorkflowState.Cancelled,
            (await cancelApplication.GetOperationAsync(context, cancelOperation.OperationId))!.State);
        Assert.Empty(cancelApplication.GetPendingOperationIds());
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

    [Fact]
    public async Task File_store_compare_and_append_allows_only_one_durable_claim_across_instances()
    {
        var root = Path.Combine(Path.GetTempPath(), "v2-effect-cas-" + Guid.NewGuid().ToString("N"));
        try
        {
            var firstStore = new FileAggregateStore(root);
            await SeedEffectAsync(firstStore);
            var secondStore = new FileAggregateStore(root);
            var first = new EffectTransitionRecord("effect", "claim-a", "Applying", null, DateTimeOffset.UtcNow);
            var second = new EffectTransitionRecord("effect", "claim-b", "Applying", null, DateTimeOffset.UtcNow);

            var results = await Task.WhenAll(
                firstStore.TryAppendEffectTransitionAsync("aggregate", "effect", null, first),
                secondStore.TryAppendEffectTransitionAsync("aggregate", "effect", null, second));

            Assert.Single(results, static result => result);
            Assert.Single((await firstStore.ReadAsync("aggregate")).EffectTransitions);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
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

    private static RuntimeRequestContext Context() => new(
        new("tenant"),
        new("workspace"),
        new("user", PrincipalKind.User),
        "session",
        AuthAssurance.Password,
        "correlation",
        "retry",
        new HashSet<string> { "brain.act", "brain.read" });

    private static CommandEnvelope Command(RuntimeRequestContext context, string suffix) => new(
        "test.command",
        2,
        "command-" + suffix,
        context,
        JsonSerializer.SerializeToElement(new { suffix }));

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

    private sealed class SequenceCommandHandler(params CommandExecutionResult[] results) : ICommandHandler
    {
        private readonly Queue<CommandExecutionResult> _results = new(results);
        public int Calls { get; private set; }
        public bool CanHandle(string commandType) => true;

        public Task<CommandExecutionResult> ExecuteAsync(CommandEnvelope command, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class FailingPersistentState : IPersistentState<AggregateGrainState>
    {
        public AggregateGrainState State { get; set; } = new();
        public string Etag { get; set; } = string.Empty;
        public bool RecordExists { get; set; }
        public bool FailWrites { get; set; }

        public Task ClearStateAsync()
        {
            State = new();
            RecordExists = false;
            return Task.CompletedTask;
        }

        public Task ReadStateAsync() => Task.CompletedTask;

        public Task WriteStateAsync()
        {
            if (FailWrites) throw new IOException("injected persistent-state failure");
            RecordExists = true;
            return Task.CompletedTask;
        }
    }
}
