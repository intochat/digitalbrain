using DigitalBrain;
using DigitalBrain.Kernel;
using Orleans.Journaling;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace DigitalBrain.Tests.Conversations;

public sealed class ConversationTurnCoordinatorTests
{
    [Theory]
    [InlineData(ConversationRole.Fast)]
    [InlineData(ConversationRole.Balanced)]
    [InlineData(ConversationRole.Reasoning)]
    public async Task Intent_and_pending_operation_are_committed_before_the_selected_role_is_invoked(
        ConversationRole role)
    {
        var state = ConversationTestState.Create();
        var commitCount = 0;
        var turnId = ConversationTurnId.New();
        var invoker = new RecordingRoleInvoker((observedRole, text, _) =>
        {
            Assert.Equal(1, commitCount);
            Assert.Equal(role, observedRole);
            Assert.Equal("hello", text);
            Assert.Equal(
                ExternalOperationStatus.Pending,
                state.Neuron.Operations[turnId.Value].Status);
            Assert.Equal("hello", state.Conversation.Intents[turnId.Value].Text);
            Assert.Equal(role, state.Conversation.Intents[turnId.Value].Role);
            return Task.FromResult("provider response");
        });
        var coordinator = CreateCoordinator(
            state,
            invoker,
            _ =>
            {
                commitCount++;
                return Task.CompletedTask;
            });

        var result = await coordinator.SubmitTurnAsync(
            new ConversationTurnRequest(turnId, role, "hello"));

        Assert.Equal("provider response", result.Response);
        Assert.Equal(2, commitCount);
        Assert.Equal(ExternalOperationStatus.Succeeded, state.Neuron.Operations[turnId.Value].Status);
        Assert.Single(state.Conversation.Results);
        Assert.Single(state.Conversation.Turns);
        Assert.Single(state.Neuron.Outbox);
    }

    [Fact]
    public async Task Recovery_is_armed_before_the_intent_commit_and_provider_invocation()
    {
        var state = ConversationTestState.Create();
        var order = new List<string>();
        var invoker = new RecordingRoleInvoker((_, _, _) =>
        {
            order.Add("provider");
            return Task.FromResult("answer");
        });
        var coordinator = CreateCoordinator(
            state,
            invoker,
            _ =>
            {
                order.Add("commit");
                return Task.CompletedTask;
            },
            _ =>
            {
                order.Add("drain");
                return Task.CompletedTask;
            },
            armRecovery: () =>
            {
                order.Add("arm");
                return Task.CompletedTask;
            });

        await coordinator.SubmitTurnAsync(
            new ConversationTurnRequest(
                ConversationTurnId.New(),
                ConversationRole.Fast,
                "hello"));

        Assert.Equal(["arm", "commit", "provider", "commit", "drain"], order);
    }

    [Fact]
    public async Task Recovery_registration_failure_is_sanitized_before_any_durable_mutation_or_provider_call()
    {
        const string secret = "reminder-storage-secret-marker";
        var state = ConversationTestState.Create();
        var invoker = new RecordingRoleInvoker((_, _, _) => Task.FromResult("must not run"));
        var coordinator = CreateCoordinator(
            state,
            invoker,
            armRecovery: () => throw new BrainException(
                NeuronFailureKind.StorageUnavailable,
                secret));

        var failure = await Assert.ThrowsAsync<BrainException>(() =>
            coordinator.SubmitTurnAsync(
                new ConversationTurnRequest(
                    ConversationTurnId.New(),
                    ConversationRole.Fast,
                    "hello")));

        Assert.Equal(NeuronFailureKind.StorageUnavailable, failure.FailureKind);
        Assert.DoesNotContain(secret, failure.Message, StringComparison.Ordinal);
        Assert.Empty(state.Conversation.Intents);
        Assert.Empty(state.Neuron.Operations);
        Assert.Equal(0, invoker.InvocationCount);
    }

    [Fact]
    public async Task Repeated_committed_turn_returns_the_same_result_without_a_second_provider_call_or_append()
    {
        var state = ConversationTestState.Create();
        var invoker = new RecordingRoleInvoker((_, _, _) => Task.FromResult("once"));
        var coordinator = CreateCoordinator(state, invoker);
        var turnId = ConversationTurnId.New();
        var request = new ConversationTurnRequest(turnId, ConversationRole.Fast, "hello");

        var first = await coordinator.SubmitTurnAsync(request);
        var second = await coordinator.SubmitTurnAsync(request);
        var snapshot = coordinator.Read();

        Assert.Equal(first, second);
        Assert.Equal(1, invoker.InvocationCount);
        Assert.Single(state.Conversation.Results);
        Assert.Single(state.Conversation.Turns);
        Assert.Single(snapshot.Turns);
        Assert.Equal(1, snapshot.Revision);
    }

    [Fact]
    public async Task Reusing_a_committed_turn_id_with_different_input_is_rejected_without_provider_call()
    {
        var state = ConversationTestState.Create();
        var invoker = new RecordingRoleInvoker((_, _, _) => Task.FromResult("once"));
        var coordinator = CreateCoordinator(state, invoker);
        var turnId = ConversationTurnId.New();
        await coordinator.SubmitTurnAsync(
            new ConversationTurnRequest(turnId, ConversationRole.Fast, "hello"));

        var failure = await Assert.ThrowsAsync<BrainException>(() =>
            coordinator.SubmitTurnAsync(
                new ConversationTurnRequest(turnId, ConversationRole.Fast, "different")));

        Assert.Equal(NeuronFailureKind.OperationFailed, failure.FailureKind);
        Assert.Equal(1, invoker.InvocationCount);
        Assert.Single(state.Conversation.Turns);
    }

    [Fact]
    public async Task Ambiguous_transport_failure_is_committed_as_unknown_without_replay()
    {
        var state = ConversationTestState.Create();
        var commitCount = 0;
        var invoker = new RecordingRoleInvoker((_, _, _) =>
            throw new HttpRequestException("provider unavailable"));
        var coordinator = CreateCoordinator(
            state,
            invoker,
            _ =>
            {
                commitCount++;
                return Task.CompletedTask;
            });
        var turnId = ConversationTurnId.New();

        var failure = await Assert.ThrowsAsync<BrainException>(() =>
            coordinator.SubmitTurnAsync(
                new ConversationTurnRequest(turnId, ConversationRole.Balanced, "hello")));

        Assert.Equal(NeuronFailureKind.OperationUnknown, failure.FailureKind);
        Assert.Equal(2, commitCount);
        Assert.Equal(ExternalOperationStatus.Unknown, state.Neuron.Operations[turnId.Value].Status);
        Assert.Empty(state.Conversation.Results);
        Assert.Empty(state.Conversation.Turns);
        Assert.Empty(state.Neuron.Outbox);
    }

    [Fact]
    public async Task Confirmed_provider_failure_is_committed_as_failed()
    {
        var state = ConversationTestState.Create();
        var invoker = new RecordingRoleInvoker((_, _, _) =>
            throw new ProviderInvocationException(
                outcomeUnknown: false,
                "provider rejected request",
                new InvalidOperationException()));
        var coordinator = CreateCoordinator(state, invoker);
        var turnId = ConversationTurnId.New();

        var failure = await Assert.ThrowsAsync<BrainException>(() =>
            coordinator.SubmitTurnAsync(
                new ConversationTurnRequest(turnId, ConversationRole.Fast, "hello")));

        Assert.Equal(NeuronFailureKind.ProviderUnavailable, failure.FailureKind);
        Assert.Equal(ExternalOperationStatus.Failed, state.Neuron.Operations[turnId.Value].Status);
        Assert.Empty(state.Conversation.Results);
        Assert.Empty(state.Neuron.Outbox);
    }

    [Fact]
    public async Task Provider_failure_disarms_recovery_after_the_terminal_state_is_committed()
    {
        var state = ConversationTestState.Create();
        var recoveryArmed = false;
        var invoker = new RecordingRoleInvoker((_, _, _) =>
            throw new ProviderInvocationException(
                outcomeUnknown: false,
                "provider rejected request",
                new InvalidOperationException()));
        var coordinator = CreateCoordinator(
            state,
            invoker,
            armRecovery: () =>
            {
                recoveryArmed = true;
                return Task.CompletedTask;
            },
            disarmRecovery: () =>
            {
                recoveryArmed = false;
                return Task.CompletedTask;
            });

        await Assert.ThrowsAsync<BrainException>(() =>
            coordinator.SubmitTurnAsync(
                new ConversationTurnRequest(
                    ConversationTurnId.New(),
                    ConversationRole.Fast,
                    "hello")));

        Assert.False(recoveryArmed);
    }

    [Fact]
    public async Task Provider_failure_keeps_recovery_armed_for_an_earlier_pending_notification()
    {
        var state = ConversationTestState.Create();
        var notificationId = Guid.NewGuid();
        state.Neuron.Outbox[notificationId] = new NeuronNotification(
            notificationId,
            Guid.NewGuid(),
            NotificationDeliveryStatus.Pending,
            AttemptCount: 1);
        var recoveryArmed = false;
        var invoker = new RecordingRoleInvoker((_, _, _) =>
            throw new ProviderInvocationException(
                outcomeUnknown: false,
                "provider rejected request",
                new InvalidOperationException()));
        var coordinator = CreateCoordinator(
            state,
            invoker,
            armRecovery: () =>
            {
                recoveryArmed = true;
                return Task.CompletedTask;
            },
            disarmRecovery: () =>
            {
                recoveryArmed = false;
                return Task.CompletedTask;
            });

        await Assert.ThrowsAsync<BrainException>(() =>
            coordinator.SubmitTurnAsync(
                new ConversationTurnRequest(
                    ConversationTurnId.New(),
                    ConversationRole.Fast,
                    "hello")));

        Assert.True(recoveryArmed);
    }

    [Fact]
    public async Task Notification_failure_cannot_erase_the_committed_result_and_snapshot()
    {
        var state = ConversationTestState.Create();
        var invoker = new RecordingRoleInvoker((_, _, _) => Task.FromResult("durable answer"));
        var drainCount = 0;
        var coordinator = CreateCoordinator(
            state,
            invoker,
            drainOutbox: _ =>
            {
                drainCount++;
                throw new BrainException(NeuronFailureKind.ProviderUnavailable, "stream unavailable");
            });
        var turnId = ConversationTurnId.New();
        var request = new ConversationTurnRequest(turnId, ConversationRole.Reasoning, "hello");

        var failure = await Assert.ThrowsAsync<BrainException>(() =>
            coordinator.SubmitTurnAsync(request));
        var repaired = await coordinator.SubmitTurnAsync(request);
        var snapshot = coordinator.Read();

        Assert.Equal(NeuronFailureKind.ProviderUnavailable, failure.FailureKind);
        Assert.Equal("durable answer", repaired.Response);
        Assert.Equal(1, invoker.InvocationCount);
        Assert.Equal(1, drainCount);
        Assert.Equal("durable answer", Assert.Single(snapshot.Turns).Response);
        Assert.Single(state.Neuron.Outbox);
    }

    [Fact]
    public async Task Notification_failure_is_sanitized_and_keeps_armed_recovery_for_the_committed_result()
    {
        const string secret = "stream-endpoint-secret-marker";
        var state = ConversationTestState.Create();
        var recoveryArmed = false;
        var invoker = new RecordingRoleInvoker((_, _, _) => Task.FromResult("durable answer"));
        var coordinator = CreateCoordinator(
            state,
            invoker,
            drainOutbox: _ => throw new BrainException(
                NeuronFailureKind.ProviderUnavailable,
                secret),
            armRecovery: () =>
            {
                recoveryArmed = true;
                return Task.CompletedTask;
            });
        var turnId = ConversationTurnId.New();

        var failure = await Assert.ThrowsAsync<BrainException>(() =>
            coordinator.SubmitTurnAsync(
                new ConversationTurnRequest(
                    turnId,
                    ConversationRole.Reasoning,
                    "hello")));

        Assert.Equal(NeuronFailureKind.ProviderUnavailable, failure.FailureKind);
        Assert.DoesNotContain(secret, failure.Message, StringComparison.Ordinal);
        Assert.True(recoveryArmed);
        Assert.Equal("durable answer", state.Conversation.Results[turnId.Value].Response);
        Assert.Equal(ExternalOperationStatus.Succeeded, state.Neuron.Operations[turnId.Value].Status);
        Assert.Contains(
            state.Neuron.Outbox.Values,
            notification => notification.DeliveryStatus == NotificationDeliveryStatus.Pending);
    }

    [Fact]
    public async Task Notification_infrastructure_cancellation_is_sanitized()
    {
        const string secret = "notification-cancellation-secret-marker";
        var state = ConversationTestState.Create();
        var coordinator = CreateCoordinator(
            state,
            new RecordingRoleInvoker((_, _, _) => Task.FromResult("durable answer")),
            drainOutbox: _ => throw new OperationCanceledException(secret));

        var failure = await Assert.ThrowsAsync<BrainException>(() =>
            coordinator.SubmitTurnAsync(
                new ConversationTurnRequest(
                    ConversationTurnId.New(),
                    ConversationRole.Fast,
                    "hello")));

        Assert.Equal(NeuronFailureKind.ProviderUnavailable, failure.FailureKind);
        Assert.DoesNotContain(secret, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_final_commit_invalidates_the_activation_before_uncommitted_state_can_be_served()
    {
        const string secret = "journal-storage-secret-marker";
        var state = ConversationTestState.Create();
        var commitCount = 0;
        var invalidationCount = 0;
        var invoker = new RecordingRoleInvoker((_, _, _) => Task.FromResult("uncommitted"));
        var coordinator = CreateCoordinator(
            state,
            invoker,
            _ =>
            {
                commitCount++;
                return commitCount == 2
                    ? throw new BrainException(
                        NeuronFailureKind.StorageUnavailable,
                        secret)
                    : Task.CompletedTask;
            },
            invalidateActivation: () => invalidationCount++);
        var request = new ConversationTurnRequest(
            ConversationTurnId.New(),
            ConversationRole.Fast,
            "hello");

        var commitFailure = await Assert.ThrowsAsync<BrainException>(() =>
            coordinator.SubmitTurnAsync(request));
        var retryFailure = await Assert.ThrowsAsync<BrainException>(() =>
            coordinator.SubmitTurnAsync(request));
        var readFailure = Assert.Throws<BrainException>(coordinator.Read);

        Assert.Equal(NeuronFailureKind.StorageUnavailable, commitFailure.FailureKind);
        Assert.DoesNotContain(secret, commitFailure.Message, StringComparison.Ordinal);
        Assert.Equal(NeuronFailureKind.StorageUnavailable, retryFailure.FailureKind);
        Assert.Equal(NeuronFailureKind.StorageUnavailable, readFailure.FailureKind);
        Assert.Equal(1, invalidationCount);
        Assert.Equal(1, invoker.InvocationCount);
    }

    [Fact]
    public async Task Failed_intent_commit_prevents_provider_invocation_and_invalidates_the_activation()
    {
        const string secret = "journal-storage-secret-marker";
        var state = ConversationTestState.Create();
        var invalidationCount = 0;
        var invoker = new RecordingRoleInvoker((_, _, _) => Task.FromResult("must not run"));
        var coordinator = CreateCoordinator(
            state,
            invoker,
            _ => throw new BrainException(
                NeuronFailureKind.StorageUnavailable,
                secret),
            invalidateActivation: () => invalidationCount++);

        var failure = await Assert.ThrowsAsync<BrainException>(() =>
            coordinator.SubmitTurnAsync(
                new ConversationTurnRequest(
                    ConversationTurnId.New(),
                    ConversationRole.Fast,
                    "hello")));

        Assert.Equal(NeuronFailureKind.StorageUnavailable, failure.FailureKind);
        Assert.DoesNotContain(secret, failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, invalidationCount);
        Assert.Equal(0, invoker.InvocationCount);
    }

    [Fact]
    public async Task Unknown_crash_outcome_is_not_replayed_without_provider_reconciliation()
    {
        var state = ConversationTestState.Create();
        var turnId = ConversationTurnId.New();
        state.Conversation.Intents[turnId.Value] =
            new ConversationTurnRequest(turnId, ConversationRole.Fast, "hello");
        state.Neuron.Operations[turnId.Value] = new ExternalOperation(
            turnId.Value,
            ExternalOperationStatus.Unknown,
            NeuronFailureKind.OperationUnknown);
        var invoker = new RecordingRoleInvoker((_, _, _) => Task.FromResult("must not run"));
        var coordinator = CreateCoordinator(state, invoker);

        var failure = await Assert.ThrowsAsync<BrainException>(() =>
            coordinator.SubmitTurnAsync(
                new ConversationTurnRequest(turnId, ConversationRole.Fast, "hello")));

        Assert.Equal(NeuronFailureKind.OperationUnknown, failure.FailureKind);
        Assert.Equal(0, invoker.InvocationCount);
        Assert.Empty(state.Conversation.Results);
    }

    [Fact]
    public void Read_returns_only_committed_turns_in_revision_order()
    {
        var state = ConversationTestState.Create();
        var firstId = ConversationTurnId.New();
        var secondId = ConversationTurnId.New();
        state.Conversation.Turns[firstId.Value] =
            new ConversationTurn(firstId, ConversationRole.Fast, "first", "one");
        state.Conversation.Results[firstId.Value] =
            new ConversationTurnResult(firstId, ConversationRole.Fast, "one", 2);
        state.Conversation.Turns[secondId.Value] =
            new ConversationTurn(secondId, ConversationRole.Balanced, "second", "two");
        state.Conversation.Results[secondId.Value] =
            new ConversationTurnResult(secondId, ConversationRole.Balanced, "two", 1);
        state.Conversation.Intents[ConversationTurnId.New().Value] =
            new ConversationTurnRequest(
                ConversationTurnId.New(),
                ConversationRole.Reasoning,
                "pending");
        state.Conversation.Revision.Value = 2;
        var coordinator = CreateCoordinator(
            state,
            new RecordingRoleInvoker((_, _, _) => Task.FromResult("unused")));

        var snapshot = coordinator.Read();

        Assert.Equal([secondId, firstId], snapshot.Turns.Select(turn => turn.TurnId));
        Assert.Equal(2, snapshot.Revision);
    }

    [Fact]
    public void Read_fails_closed_when_a_committed_result_has_no_committed_turn()
    {
        var state = ConversationTestState.Create();
        var turnId = ConversationTurnId.New();
        state.Conversation.Results[turnId.Value] =
            new ConversationTurnResult(
                turnId,
                ConversationRole.Fast,
                "orphan",
                1);
        state.Conversation.Revision.Value = 1;
        var coordinator = CreateCoordinator(
            state,
            new RecordingRoleInvoker((_, _, _) => Task.FromResult("unused")));

        var failure = Assert.Throws<BrainException>(coordinator.Read);

        Assert.Equal(NeuronFailureKind.StorageUnavailable, failure.FailureKind);
    }

    private static ConversationTurnCoordinator CreateCoordinator(
        ConversationTestState state,
        IConversationRoleInvoker invoker,
        Func<CancellationToken, Task>? commit = null,
        Func<CancellationToken, Task>? drainOutbox = null,
        Action? invalidateActivation = null,
        Func<Task>? armRecovery = null,
        Func<Task>? disarmRecovery = null) =>
        new(
            state.Neuron,
            state.Conversation,
            invoker,
            commit ?? (_ => Task.CompletedTask),
            drainOutbox ?? (_ => Task.CompletedTask),
            armRecovery ?? (() => Task.CompletedTask),
            disarmRecovery ?? (() => Task.CompletedTask),
            new ConversationActivationGuard(),
            invalidateActivation ?? (() => { }));

    private sealed class RecordingRoleInvoker(
        Func<ConversationRole, string, CancellationToken, Task<string>> invoke)
        : IConversationRoleInvoker
    {
        public int InvocationCount { get; private set; }

        public Task<string> CompleteAsync(
            ConversationRole role,
            string text,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return invoke(role, text, cancellationToken);
        }
    }
}

internal sealed record ConversationTestState(
    NeuronDurableState Neuron,
    ConversationDurableState Conversation)
{
    public static ConversationTestState Create() =>
        new(
            new NeuronDurableState(
                new TestDurableValue<NeuronStatus>(),
                new TestDurableDictionary<Guid, ExternalOperation>(),
                new TestDurableDictionary<Guid, NeuronNotification>()),
            new ConversationDurableState(
                new TestDurableDictionary<Guid, ConversationTurnRequest>(),
                new TestDurableDictionary<Guid, ConversationTurn>(),
                new TestDurableDictionary<Guid, ConversationTurnResult>(),
                new TestDurableValue<long>()));
}

internal sealed class TestDurableDictionary<TKey, TValue>
    : Dictionary<TKey, TValue>, IDurableDictionary<TKey, TValue>
    where TKey : notnull;

internal sealed class TestDurableValue<T> : IDurableValue<T>
{
    [AllowNull]
    public T Value { get; set; } = default!;
}
