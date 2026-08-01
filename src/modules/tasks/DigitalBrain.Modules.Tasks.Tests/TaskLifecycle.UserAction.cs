using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tasks.Tests;

public sealed partial class TaskLifecycle
{
    [Fact(DisplayName =
        "UserActionRequired contract carries only task/attempt/module/display/action-ref/epoch/park-revision/expiry/completer — never secret provider fields")]
    public void UserActionRequiredContractOmitsSecretProviderFields()
    {
        var properties = typeof(UserActionRequired)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(UserActionRequired.ActionEpoch),
                nameof(UserActionRequired.ActionReference),
                nameof(UserActionRequired.Attempt),
                nameof(UserActionRequired.Completer),
                nameof(UserActionRequired.DisplayText),
                nameof(UserActionRequired.ExpiresAt),
                nameof(UserActionRequired.Module),
                nameof(UserActionRequired.ModuleId),
                nameof(UserActionRequired.ParkRevision),
                nameof(UserActionRequired.Task),
            ],
            properties);

        Assert.DoesNotContain(
            properties,
            name => name is "SignInUrl"
                or "State"
                or "AuthorizationCode"
                or "Code"
                or "Token"
                or "AccessToken"
                or "RefreshToken"
                or "ProviderResponse"
                or "Plaintext"
                or "ProtectedBytes"
                or "Ciphertext"
                or "AuthorityProof");

        var fields = typeof(UserActionRequired)
            .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Empty(fields);
    }

    [Fact(DisplayName =
        "Running task parks on authorized UserActionRequired with one blocker, same attempt and attempt count")]
    public async Task AuthorizedUserActionParksRunningTaskWithSameAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);

        var expiresAt = brain.Clock.UtcNow.AddMinutes(30);
        var goal = new UserActionParkGoal(
            "park-me",
            ModuleId: "google.gmail",
            DisplayText: "Connect Gmail to continue",
            expiresAt,
            ModuleName: "gmail-module");

        var (worker, task, started) = await StartAsync(brain, "user-action-park", goal);
        var accepted = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        Assert.Equal(started.ActiveAttempt, accepted.Synapse.Attempt);

        var required = await task.Incoming.NextAsync<UserActionRequired>(cancellationToken);
        Assert.Equal(task.Id, required.Synapse.Task);
        Assert.Equal(started.ActiveAttempt, required.Synapse.Attempt);
        Assert.Equal("google.gmail", required.Synapse.ModuleId);
        Assert.Equal("Connect Gmail to continue", required.Synapse.DisplayText);
        Assert.Equal(worker.Id, required.Caller);
        Assert.Equal(worker.Id, required.Synapse.Completer);
        Assert.Equal(started.Revision, required.Synapse.ParkRevision);
        Assert.NotEqual(Guid.Empty, required.Synapse.ActionEpoch);

        var waiting = await WaitForStateAsync(task, TaskState.Waiting, cancellationToken);
        Assert.Equal(started.ActiveAttempt, waiting.ActiveAttempt);
        Assert.Equal(started.AttemptCount, waiting.AttemptCount);
        Assert.Equal(1, waiting.AttemptCount);
        var blocker = Assert.IsType<UserActionPending>(waiting.Blocker);
        Assert.Equal("google.gmail", blocker.ModuleId);
        Assert.Equal("Connect Gmail to continue", blocker.DisplayText);
        Assert.Equal(required.Synapse.ActionReference, blocker.ActionReference);
        Assert.Equal(required.Synapse.ActionEpoch, blocker.ActionEpoch);
        Assert.Equal(required.Synapse.ParkRevision, blocker.ParkRevision);
        Assert.Equal(worker.Id, blocker.Completer);

        // Task emits ParkReady to the bound worker completer; harness must consume it after binding checks.
        var parkReady = await worker.Incoming.NextAsync<UserActionParkReady>(cancellationToken);
        Assert.Equal(task.Id, parkReady.Caller);
        Assert.Equal(task.Id, parkReady.Synapse.Task);
        Assert.Equal(started.ActiveAttempt, parkReady.Synapse.Attempt);
        Assert.Equal(required.Synapse.Module, parkReady.Synapse.Module);
        Assert.Equal(required.Synapse.ModuleId, parkReady.Synapse.ModuleId);
        Assert.Equal(required.Synapse.ActionReference.Id, parkReady.Synapse.ActionReference.Id);
        Assert.Equal(required.Synapse.ActionEpoch, parkReady.Synapse.ActionEpoch);
        Assert.Equal(required.Synapse.ParkRevision, parkReady.Synapse.ParkRevision);
        Assert.Equal(worker.Id, parkReady.Synapse.Completer);

        var serialized = JsonSerializer.Serialize(waiting);
        Assert.DoesNotContain("https://", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sign-in", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization_code", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AuthorityProof", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("harness-state", serialized, StringComparison.Ordinal);
    }

    [Fact(DisplayName =
        "Authorized Completer CompleteUserAction resumes same Task and Attempt via Continue, clears blocker, no retry")]
    public async Task AuthorizedCompleteUserActionResumesSameAttemptViaContinue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);

        var goal = new UserActionParkGoal(
            "resume-me",
            ModuleId: "salesforce",
            DisplayText: "Authorize Salesforce",
            brain.Clock.UtcNow.AddHours(1),
            ModuleName: "salesforce-module");

        var (worker, task, started) = await StartAsync(brain, "user-action-resume", goal);
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var required = await task.Incoming.NextAsync<UserActionRequired>(cancellationToken);
        var waiting = await WaitForStateAsync(task, TaskState.Waiting, cancellationToken);

        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new CompleteParkedUserAction(
                task.Id,
                required.Synapse.ActionReference,
                required.Synapse.ActionEpoch,
                required.Synapse.ParkRevision),
            cancellationToken);

        var completed = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        Assert.Equal(started.ActiveAttempt, completed.ActiveAttempt);
        Assert.Equal(started.AttemptCount, completed.AttemptCount);
        Assert.Null(completed.Blocker);
        Assert.Equal(waiting.Revision + 1, completed.Revision);
        Assert.Null(completed.RetryOf);

        var continueEnvelopes = await WaitForAsync(
            () => task.Outgoing.ReadAsync<RelayWorkerContinue>(afterSequence: 0, cancellationToken),
            list => list.Count > 0,
            cancellationToken);
        var envelope = Assert.Single(continueEnvelopes);
        Assert.Equal(worker.Id, envelope.Synapse.Worker);
        Assert.Equal(started.ActiveAttempt, envelope.Synapse.Cursor.Attempt);
        Assert.Equal(completed.Revision, envelope.Synapse.Cursor.Revision);

        var succeeded = await task.Incoming.NextAsync<AttemptSucceeded>(cancellationToken);
        Assert.Equal(started.ActiveAttempt, succeeded.Synapse.Attempt);
        Assert.Equal(completed.Revision, succeeded.Synapse.Revision);

        var terminal = await WaitForStateAsync(task, TaskState.Succeeded, cancellationToken);
        Assert.Equal(started.AttemptCount, terminal.AttemptCount);
        Assert.Null(terminal.ActiveAttempt);
        Assert.Equal(TaskFixtures.Done, terminal.Result);
    }

    [Fact(DisplayName =
        "Wrong completer/module/task/attempt/ref/epoch, forged/expired, snapshot-only, and duplicate completion fail closed")]
    public async Task UserActionAuthorityNegativesFailClosed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);

        var goal = new UserActionParkGoal(
            "authority",
            ModuleId: "google.gmail",
            DisplayText: "Connect Gmail",
            brain.Clock.UtcNow.AddHours(1),
            ModuleName: "gmail-authority");

        var (worker, task, started) = await StartAsync(brain, "user-action-authority", goal);
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var required = await task.Incoming.NextAsync<UserActionRequired>(cancellationToken);
        _ = await WaitForStateAsync(task, TaskState.Waiting, cancellationToken);

        var forged = new ProtectedPayloadReference(Guid.NewGuid(), brain.Clock.UtcNow.AddHours(1));

        // Session Fire is outbox-drained; negatives prove fail-closed by stable Waiting state.
        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new CompleteParkedUserAction(
                task.Id,
                forged,
                required.Synapse.ActionEpoch,
                required.Synapse.ParkRevision),
            cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        Assert.Equal(TaskState.Waiting, (await task.Reference.Read()).State);

        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new CompleteParkedUserAction(
                task.Id,
                required.Synapse.ActionReference,
                required.Synapse.ActionEpoch,
                required.Synapse.ParkRevision + 99),
            cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        Assert.Equal(TaskState.Waiting, (await task.Reference.Read()).State);

        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new CompleteParkedUserAction(
                task.Id,
                required.Synapse.ActionReference,
                Guid.NewGuid(),
                required.Synapse.ParkRevision),
            cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        Assert.Equal(TaskState.Waiting, (await task.Reference.Read()).State);

        // Non-completer session Send cannot resume (Task stays Waiting).
        await brain.Client.SendAsync(
            task.Id,
            new CompleteUserAction(
                CommandId.New(),
                required.Synapse.ActionReference,
                required.Synapse.ActionEpoch,
                required.Synapse.ParkRevision),
            cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        Assert.Equal(TaskState.Waiting, (await task.Reference.Read()).State);

        var snapshot = await task.Reference.Read();
        var blocker = Assert.IsType<UserActionPending>(snapshot.Blocker);
        Assert.Equal(worker.Id, blocker.Completer);

        var stillWaiting = await task.Reference.Read();
        Assert.Equal(TaskState.Waiting, stillWaiting.State);
        Assert.Equal(started.ActiveAttempt, stillWaiting.ActiveAttempt);
        Assert.IsType<UserActionPending>(stillWaiting.Blocker);

        await brain.Client.SendAsync(
            task.Id,
            new UserActionRequired(
                task.Id,
                started.ActiveAttempt!.Value,
                new NeuronId("forged.module", task.Id.Owner, "forged"),
                "forged.module",
                "Forged action",
                forged,
                Guid.NewGuid(),
                required.Synapse.ParkRevision,
                brain.Clock.UtcNow.AddHours(1),
                worker.Id),
            cancellationToken);

        var afterForeign = await task.Reference.Read();
        Assert.Equal(TaskState.Waiting, afterForeign.State);
        Assert.Equal(started.ActiveAttempt, afterForeign.ActiveAttempt);
        var pending = Assert.IsType<UserActionPending>(afterForeign.Blocker);
        Assert.Equal(required.Synapse.ActionReference, pending.ActionReference);
        Assert.Equal("google.gmail", pending.ModuleId);

        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new CompleteParkedUserAction(
                task.Id,
                required.Synapse.ActionReference,
                required.Synapse.ActionEpoch,
                required.Synapse.ParkRevision),
            cancellationToken);

        var first = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        Assert.Equal(started.ActiveAttempt, first.ActiveAttempt);
        Assert.Null(first.Blocker);

        // Duplicate completer redelivery after resume fails closed (not waiting / no completer binding).
        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new CompleteParkedUserAction(
                task.Id,
                required.Synapse.ActionReference,
                required.Synapse.ActionEpoch,
                required.Synapse.ParkRevision),
            cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        var afterDuplicate = await task.Reference.Read();
        Assert.NotEqual(TaskState.Waiting, afterDuplicate.State);
        Assert.Null(afterDuplicate.Blocker);

        // Late redelivery after continue cannot re-park (revision/epoch binding).
        await brain.Client.SendAsync(
            task.Id,
            required.Synapse,
            cancellationToken);
        var afterStale = await task.Reference.Read();
        Assert.NotEqual(TaskState.Waiting, afterStale.State);
        Assert.Null(afterStale.Blocker);
    }

    [Fact(DisplayName =
        "Legitimate completer with wrong/stale action epoch, action reference, or park revision is permanently refused (NeuronAuthorizationException), not retried")]
    public async Task LegitimateCompleterStaleBindingsArePermanentlyRefusedNotRetried()
    {
        // Only the exact pre-park Running/Pending ordering case may throw InvalidOperationException
        // (outbox-retryable deferral). Wrong epoch/ref/revision while parked must permanently refuse
        // via NeuronAuthorizationException so outbox does not keep retrying malformed completions.
        // Disposition is observed through the real completer worker Deliver path (not client-forged
        // INeuron.Deliver, which is refused before Task authority runs).
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);

        var goal = new UserActionParkGoal(
            "stale-binding",
            ModuleId: "google.gmail",
            DisplayText: "Connect Gmail",
            brain.Clock.UtcNow.AddHours(1),
            ModuleName: "gmail-stale-binding");

        var (worker, task, started) = await StartAsync(brain, "user-action-stale-binding", goal);
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var required = await task.Incoming.NextAsync<UserActionRequired>(cancellationToken);
        var waiting = await WaitForStateAsync(task, TaskState.Waiting, cancellationToken);
        Assert.Equal(worker.Id, Assert.IsType<UserActionPending>(waiting.Blocker).Completer);

        var forgedReference = new ProtectedPayloadReference(Guid.NewGuid(), brain.Clock.UtcNow.AddHours(1));

        await AssertPermanentRefusalAsync(
            brain,
            worker,
            task.Id,
            required.Synapse.ActionReference,
            Guid.NewGuid(),
            required.Synapse.ParkRevision,
            cancellationToken);

        await AssertPermanentRefusalAsync(
            brain,
            worker,
            task.Id,
            forgedReference,
            required.Synapse.ActionEpoch,
            required.Synapse.ParkRevision,
            cancellationToken);

        await AssertPermanentRefusalAsync(
            brain,
            worker,
            task.Id,
            required.Synapse.ActionReference,
            required.Synapse.ActionEpoch,
            required.Synapse.ParkRevision + 99,
            cancellationToken);

        var stillWaiting = await task.Reference.Read();
        Assert.Equal(TaskState.Waiting, stillWaiting.State);
        Assert.Equal(started.ActiveAttempt, stillWaiting.ActiveAttempt);
        Assert.Equal(waiting.Revision, stillWaiting.Revision);
        Assert.IsType<UserActionPending>(stillWaiting.Blocker);

        // Legitimate binding still resumes after permanent refusals (no poison / no false resume).
        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new CompleteParkedUserAction(
                task.Id,
                required.Synapse.ActionReference,
                required.Synapse.ActionEpoch,
                required.Synapse.ParkRevision),
            cancellationToken);

        var resumed = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
        Assert.Equal(started.ActiveAttempt, resumed.ActiveAttempt);
        Assert.Null(resumed.Blocker);
        Assert.Equal(waiting.Revision + 1, resumed.Revision);
    }

    private static async Task AssertPermanentRefusalAsync(
        TestBrain brain,
        TestNeuron<IWorker> worker,
        NeuronId task,
        ProtectedPayloadReference actionReference,
        Guid actionEpoch,
        long expectedParkRevision,
        CancellationToken cancellationToken)
    {
        var probeId = Guid.NewGuid();
        UserActionCompletionDispositionProbe.Clear(probeId);

        await brain.Client.SendAsync<IWorker>(
            worker.Id.Name,
            new ProbeUserActionCompletionDisposition(
                probeId,
                task,
                actionReference,
                actionEpoch,
                expectedParkRevision),
            cancellationToken);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        string? disposition = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (UserActionCompletionDispositionProbe.TryRead(probeId, out disposition)
                && !string.IsNullOrWhiteSpace(disposition))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }

        Assert.False(string.IsNullOrWhiteSpace(disposition), "Completer probe did not record a disposition.");
        Assert.StartsWith(
            $"{nameof(NeuronAuthorizationException)}:",
            disposition,
            StringComparison.Ordinal);
    }

    [Fact(DisplayName =
        "Expired user-action reference cannot resume; denial fails safely without provider leakage")]
    public async Task ExpiredRefAndDenialFailSafelyWithoutProviderLeakage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);

        var expiresAt = brain.Clock.UtcNow.AddMinutes(5);
        var goal = new UserActionParkGoal(
            "deny-expire",
            ModuleId: "google.gmail",
            DisplayText: "Connect Gmail",
            expiresAt,
            ModuleName: "gmail-expire");

        var (worker, task, started) = await StartAsync(brain, "user-action-deny", goal);
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var required = await task.Incoming.NextAsync<UserActionRequired>(cancellationToken);
        _ = await WaitForStateAsync(task, TaskState.Waiting, cancellationToken);
        // Completer rendezvous must land before authority probes; otherwise harness completion can
        // race the Task's ParkReady outbox drain under serialized non-reentrant turns.
        _ = await worker.Incoming.NextAsync<UserActionParkReady>(cancellationToken);
        _ = await WaitForAsync(
            task.HasOutboxWakeupAsync,
            hasWakeup => !hasWakeup,
            cancellationToken);

        await brain.Clock.AdvanceAsync(TimeSpan.FromMinutes(6), cancellationToken);
        await AssertPermanentRefusalAsync(
            brain,
            worker,
            task.Id,
            required.Synapse.ActionReference,
            required.Synapse.ActionEpoch,
            required.Synapse.ParkRevision,
            cancellationToken);

        var stillWaiting = await task.Reference.Read();
        Assert.Equal(TaskState.Waiting, stillWaiting.State);
        Assert.Equal(started.ActiveAttempt, stillWaiting.ActiveAttempt);

        var denyGoal = new UserActionParkGoal(
            "deny-live",
            ModuleId: "google.gmail",
            DisplayText: "Connect Gmail",
            brain.Clock.UtcNow.AddHours(1),
            ModuleName: "gmail-deny-live");
        var (denyWorker, denyTask, denyStarted) = await StartAsync(brain, "user-action-deny-live", denyGoal);
        _ = await denyTask.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var denyRequired = await denyTask.Incoming.NextAsync<UserActionRequired>(cancellationToken);
        var denyWaiting = await WaitForStateAsync(denyTask, TaskState.Waiting, cancellationToken);
        _ = await denyWorker.Incoming.NextAsync<UserActionParkReady>(cancellationToken);
        _ = await WaitForAsync(
            denyTask.HasOutboxWakeupAsync,
            hasWakeup => !hasWakeup,
            cancellationToken);

        await brain.Client.SendAsync<IWorker>(
            denyWorker.Id.Name,
            new DenyParkedUserAction(
                denyTask.Id,
                denyRequired.Synapse.ActionReference,
                denyRequired.Synapse.ActionEpoch,
                denyRequired.Synapse.ParkRevision),
            cancellationToken);

        var denied = await WaitForStateAsync(denyTask, TaskState.Failed, cancellationToken);
        Assert.Null(denied.ActiveAttempt);
        Assert.Null(denied.Blocker);
        var failure = Assert.IsType<UserActionDenied>(denied.Failure);
        Assert.Equal("google.gmail", failure.ModuleId);
        Assert.Equal(denyStarted.AttemptCount, denied.AttemptCount);
        Assert.Equal(denyWaiting.ActiveAttempt, denyStarted.ActiveAttempt);

        var payload = JsonSerializer.Serialize(denied);
        Assert.DoesNotContain("https://", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("oauth", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization_code", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", payload, StringComparison.Ordinal);
    }

    [Fact(DisplayName =
        "Source-neuron outbox → DispatchWorkerContinue: production DeliveryAttemptTimeout cancels the handler token and aborts validation before restage; pending stays retryable")]
    public async Task OutboxDeliveredDispatchWorkerContinueReceivesCancelableToken()
    {
        // Runtime proof must observe Neuron.Outbox's production attempt timeout cancel the Deliver
        // token mid-validation — not a test-owned CTS and not merely CanBeCanceled. Harness arms a
        // cancellation-aware gate that blocks until that token fires; restage must not occur;
        // OCE leaves the outbox entry pending (retryable / not acknowledged). Dispose brain after
        // first observed timeout so retries do not leak for another 30s cycle.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);

        var goal = new ProgressGoal("continue-cancel-token");
        var (worker, task, started) = await StartAsync(brain, "continue-cancel-token", goal);
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        Assert.NotNull(started.ActiveAttempt);
        ContinueCancellationProbe.Clear(worker.Id);
        ContinueCancellationGate.Arm(worker.Id);
        try
        {
            await brain.Client.SendAsync(
                worker.Id,
                new DispatchWorkerContinue(new AttemptCursor(
                    task.Id,
                    worker.Id,
                    started.ActiveAttempt.Value,
                    started.Revision)),
                cancellationToken);

            // Production DeliveryAttemptTimeout is 30s; allow enough over that bound.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
            ContinueCancellationObservation? observation = null;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ContinueCancellationProbe.TryRead(worker.Id, out observation)
                    && observation is { HandlerTokenCanceledDuringValidationGate: true })
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }

            Assert.NotNull(observation);
            Assert.True(
                observation.HandlerTokenCanceledDuringValidationGate,
                $"Production outbox attempt timeout must cancel the handler token inside the validation gate. Observed: {observation}");
            Assert.False(
                observation.RestagedContinue,
                "Cancelled attempt must abort before Continue restage.");
            Assert.False(
                observation.DeliveryAcknowledgedByRestage,
                "Cancelled attempt must not acknowledge/restage the delivery.");
            Assert.True(
                observation.ValidationGateEntries >= 1,
                "Validation gate must have been entered under the production Deliver attempt token.");
            Assert.True(
                observation.HandlerTokenCanBeCanceled && !observation.HandlerTokenIsDefault,
                $"Handler token must be cancelable/non-default (production attempt CTS). Observed: {observation}");

            // Pending remains retryable: a second drain attempt re-enters the gate (RetryInterval
            // is 50ms) before we dispose — proves the outbox left the entry unacked.
            var reentryDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (ContinueCancellationGate.EntryCount(worker.Id) < 2
                   && DateTime.UtcNow < reentryDeadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            }

            Assert.True(
                ContinueCancellationGate.EntryCount(worker.Id) >= 2,
                "After attempt-timeout cancel, outbox must leave the delivery pending so a subsequent drain re-enters the gate (retryable, not acknowledged).");
            Assert.False(
                observation.RestagedContinue || observation.DeliveryAcknowledgedByRestage,
                "No successful restage/ack may have been recorded before dispose.");

            // Source guard remains secondary to the runtime timeout proof.
            var outboxPath = Path.Combine(
                FindRepositoryRoot(),
                "src",
                "core",
                "kernel",
                "DigitalBrain",
                "Neuron",
                "Neuron.Outbox.cs");
            Assert.True(File.Exists(outboxPath), $"Missing Neuron.Outbox at {outboxPath}");
            var outbox = await File.ReadAllTextAsync(outboxPath, cancellationToken);
            Assert.DoesNotContain(
                "await Deliver(entry.Delivery);",
                outbox,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "await GrainFactory.GetGrain<INeuron>(receiver.ToGrainId()).Deliver(entry.Delivery);",
                outbox,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "DrainAsync(CancellationToken.None)",
                outbox,
                StringComparison.Ordinal);
            Assert.Contains("DeliveryAttemptTimeout", outbox, StringComparison.Ordinal);
            Assert.Contains("CancelAfter", outbox, StringComparison.Ordinal);
        }
        finally
        {
            ContinueCancellationGate.Disarm(worker.Id);
            ContinueCancellationProbe.Clear(worker.Id);
        }
    }

    [Theory(DisplayName =
        "Complete/Deny user-action handling that faults the sole final outer-turn journal write must not leave terminal state/receipt without its continuation/terminal effect; redrive converges exactly once")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompleteOrDenyTurnCommitFaultDoesNotPersistTerminalWithoutEffect(bool complete)
    {
        // Complete/Deny stage terminal/receipt then commit once on the outer turn (no intermediate
        // Task journal writes). Zero allowed commits before fault targets that sole final write so
        // the injector is not consumed by an earlier commit; turn staging rollback + retraction
        // leave Waiting so redrive converges exactly once.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);

        var label = complete ? "atomic-complete" : "atomic-deny";
        var goal = new UserActionParkGoal(
            label,
            ModuleId: "google.gmail",
            DisplayText: "Connect Gmail",
            brain.Clock.UtcNow.AddHours(1),
            ModuleName: $"{label}-module");

        var (worker, task, started) = await StartAsync(brain, label, goal);
        _ = await task.Incoming.NextAsync<AttemptAccepted>(cancellationToken);
        var required = await task.Incoming.NextAsync<UserActionRequired>(cancellationToken);
        var waiting = await WaitForStateAsync(task, TaskState.Waiting, cancellationToken);
        Assert.Equal(started.ActiveAttempt, waiting.ActiveAttempt);

        // Park-ready rendezvous must land on the worker before we arm the sole-final-write fault.
        // Task outbox delivery+ack is itself a journal write; if still in flight when the fault is
        // armed with zero allowed commits, that older ack can consume the injector instead of the
        // complete/deny outer turn. Boundedly wait for outbox wakeup absence after delivery proof.
        _ = await worker.Incoming.NextAsync<UserActionParkReady>(cancellationToken);
        _ = await WaitForAsync(
            task.HasOutboxWakeupAsync,
            hasWakeup => !hasWakeup,
            cancellationToken);

        await using (var fault = task.FailJournalCommitAfter(
            allowCommitsBeforeFault: 0,
            message: complete
                ? "task complete sole final outer-turn journal write fails"
                : "task deny sole final outer-turn journal write fails"))
        {
            if (complete)
            {
                await brain.Client.SendAsync<IWorker>(
                    worker.Id.Name,
                    new CompleteParkedUserAction(
                        task.Id,
                        required.Synapse.ActionReference,
                        required.Synapse.ActionEpoch,
                        required.Synapse.ParkRevision),
                    cancellationToken);
            }
            else
            {
                await brain.Client.SendAsync<IWorker>(
                    worker.Id.Name,
                    new DenyParkedUserAction(
                        task.Id,
                        required.Synapse.ActionReference,
                        required.Synapse.ActionEpoch,
                        required.Synapse.ParkRevision),
                    cancellationToken);
            }

            var faultDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (!fault.IsConsumed && DateTime.UtcNow < faultDeadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            }

            Assert.True(
                fault.IsConsumed,
                "Task journal fault must fire on the sole final outer-turn commit during complete/deny handling.");
        }

        await brain.Clock.AdvanceAsync(TimeSpan.FromSeconds(2), cancellationToken);
        await task.RestartHostAsync(cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        task = brain.Neuron<ITask>($"{label}-task");
        worker = brain.Neuron<IWorker>($"{label}-worker");

        // Faulted sole final commit leaves no durable terminal state/receipt/effect.
        // Correct atomic surface: still Waiting with blocker so redrive can converge exactly once.
        var afterFault = await task.Reference.Read()
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        Assert.Equal(TaskState.Waiting, afterFault.State);
        Assert.NotNull(afterFault.Blocker);
        Assert.Equal(started.ActiveAttempt, afterFault.ActiveAttempt);

        if (complete)
        {
            await brain.Client.SendAsync<IWorker>(
                worker.Id.Name,
                new CompleteParkedUserAction(
                    task.Id,
                    required.Synapse.ActionReference,
                    required.Synapse.ActionEpoch,
                    required.Synapse.ParkRevision),
                cancellationToken);

            var resumed = await WaitForStateAsync(task, TaskState.Running, cancellationToken);
            Assert.Equal(started.ActiveAttempt, resumed.ActiveAttempt);
            Assert.Null(resumed.Blocker);
            Assert.Equal(waiting.Revision + 1, resumed.Revision);

            var continueEnvelopes = await WaitForAsync(
                () => task.Outgoing.ReadAsync<RelayWorkerContinue>(afterSequence: 0, cancellationToken),
                list => list.Count > 0,
                cancellationToken);
            Assert.Single(continueEnvelopes);
        }
        else
        {
            await brain.Client.SendAsync<IWorker>(
                worker.Id.Name,
                new DenyParkedUserAction(
                    task.Id,
                    required.Synapse.ActionReference,
                    required.Synapse.ActionEpoch,
                    required.Synapse.ParkRevision),
                cancellationToken);

            var denied = await WaitForStateAsync(task, TaskState.Failed, cancellationToken);
            Assert.Null(denied.Blocker);
            Assert.IsType<UserActionDenied>(denied.Failure);
            Assert.Equal(1, denied.AttemptCount);
        }
    }

    [Fact(DisplayName =
        "CompleteUserAction must stage Task→relay dispatch on the turn-owned no-write path (not TryDispatchPendingAsync), and Deny must StageForTurn without intermediate journal writes")]
    public void CompleteAndDenyUserActionMustNotSaveAsyncBeforeTurnCommit()
    {
        var tasksRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "modules",
            "tasks",
            "DigitalBrain.Modules.Tasks");
        var userActionsPath = Path.Combine(tasksRoot, "TaskNeuron.UserActions.cs");
        var dispatchPath = Path.Combine(tasksRoot, "TaskNeuron.Dispatch.cs");
        Assert.True(File.Exists(userActionsPath), $"Missing TaskNeuron.UserActions at {userActionsPath}");
        Assert.True(File.Exists(dispatchPath), $"Missing TaskNeuron.Dispatch at {dispatchPath}");
        var userActions = File.ReadAllText(userActionsPath);
        var dispatch = File.ReadAllText(dispatchPath);

        var completeMatch = Regex.Match(
            userActions,
            @"public\s+async\s+Task\s+HandleAsync\s*\(\s*CompleteUserAction\s+command\s*,\s*CancellationToken\s+cancellationToken\s*\)\s*\{(?<body>.*?)(?=\n\s{4}public\s+)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(completeMatch.Success, "Could not locate CompleteUserAction handler body.");
        var completeBody = completeMatch.Groups["body"].Value;
        Assert.DoesNotContain("SaveAsync", completeBody, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteStateAsync", completeBody, StringComparison.Ordinal);
        Assert.Contains("StageForTurn", completeBody, StringComparison.Ordinal);
        // Complete must use the explicit turn-staged dispatch path. The durable helper
        // journal-clears PendingDispatch mid-turn and would materialize Running/receipt
        // before the kernel outer commit.
        Assert.DoesNotContain("TryDispatchPendingAsync", completeBody, StringComparison.Ordinal);
        Assert.Contains("StagePendingDispatchForTurnAsync", completeBody, StringComparison.Ordinal);

        var denyMatch = Regex.Match(
            userActions,
            @"public\s+async\s+Task\s+HandleAsync\s*\(\s*DenyUserAction\s+command\s*,\s*CancellationToken\s+cancellationToken\s*\)\s*\{(?<body>.*?)(?=\n\s{4}private\s+|\n\s{4}public\s+|\n\})",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(denyMatch.Success, "Could not locate DenyUserAction handler body.");
        var denyBody = denyMatch.Groups["body"].Value;
        Assert.DoesNotContain("SaveAsync", denyBody, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteStateAsync", denyBody, StringComparison.Ordinal);
        Assert.Contains("StageForTurn", denyBody, StringComparison.Ordinal);

        // Contract of the turn-staged helper itself: buffers via SendAsync, clears PendingDispatch
        // only through StageForTurn, never journal-writes or unregisters the dispatch reminder.
        var stagedMatch = Regex.Match(
            dispatch,
            @"private\s+async\s+Task\s+StagePendingDispatchForTurnAsync\s*\(\s*\)\s*\{(?<body>.*?)(?=\n\s{4}private\s+|\n\s{4}\[|\n\})",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(
            stagedMatch.Success,
            "Could not locate StagePendingDispatchForTurnAsync — Complete's turn-atomic dispatch path must exist as an explicit helper.");
        var stagedBody = stagedMatch.Groups["body"].Value;
        Assert.Contains("SendAsync", stagedBody, StringComparison.Ordinal);
        Assert.Contains("StageForTurn", stagedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync", stagedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteStateAsync", stagedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("UnregisterReminderAsync", stagedBody, StringComparison.Ordinal);

        // Durable reminder/out-of-turn path remains the journal-writing ownership transfer.
        var durableMatch = Regex.Match(
            dispatch,
            @"private\s+async\s+Task\s+TryDispatchPendingAsync\s*\(\s*\)\s*\{(?<body>.*?)(?=\n\s{4}private\s+|\n\s{4}\[|\n\})",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(durableMatch.Success, "Could not locate TryDispatchPendingAsync durable dispatch path.");
        var durableBody = durableMatch.Groups["body"].Value;
        Assert.Contains("SaveAsync", durableBody, StringComparison.Ordinal);
    }

    [Fact(DisplayName =
        "Same owner + same action epoch (caller-chosen CommandId) cannot alias a different task/attempt/module into existing custody; permanent refusal")]
    public async Task SameEpochCannotAliasDifferentTaskAttemptOrModuleBinding()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var time = TimeProvider.System;
        var custody = new MemoryUserActionCustody(time);
        var owner = new OwnerId("epoch-alias-owner");
        var epoch = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var taskA = NeuronId.For<ITask>(owner, "task-a");
        var taskB = NeuronId.For<ITask>(owner, "task-b");
        var attemptA = new AttemptId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var attemptB = new AttemptId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var moduleA = new NeuronId("google.gmail", owner, "gmail-a");
        var moduleB = new NeuronId("salesforce", owner, "sf-b");
        var completer = UserActionCompletionBridge.For(owner, epoch);
        var materialA = ModuleUserActionBoundary.ProtectActionMaterial(
            new Uri("https://auth.example.test/a"),
            "state-a");
        var materialB = ModuleUserActionBoundary.ProtectActionMaterial(
            new Uri("https://auth.example.test/b"),
            "state-b");

        var first = await custody.IssueAsync(
            owner,
            taskA,
            attemptA,
            moduleA,
            "google.gmail",
            "Connect Gmail",
            materialA,
            parkRevision: 0,
            lifetime: TimeSpan.FromHours(1),
            completer,
            epoch,
            cancellationToken);
        Assert.Equal(epoch, first.Requirement.ActionEpoch);
        Assert.Equal(taskA, first.Requirement.Task);

        // Exact redelivery of the same binding remains quiet/idempotent.
        var same = await custody.IssueAsync(
            owner,
            taskA,
            attemptA,
            moduleA,
            "google.gmail",
            "Connect Gmail",
            materialA,
            parkRevision: 0,
            lifetime: TimeSpan.FromHours(1),
            completer,
            epoch,
            cancellationToken);
        Assert.Equal(first.Requirement.ActionReference, same.Requirement.ActionReference);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await custody.IssueAsync(
                owner,
                taskB,
                attemptA,
                moduleA,
                "google.gmail",
                "Connect Gmail",
                materialA,
                parkRevision: 0,
                lifetime: TimeSpan.FromHours(1),
                completer,
                epoch,
                cancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await custody.IssueAsync(
                owner,
                taskA,
                attemptB,
                moduleA,
                "google.gmail",
                "Connect Gmail",
                materialA,
                parkRevision: 0,
                lifetime: TimeSpan.FromHours(1),
                completer,
                epoch,
                cancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await custody.IssueAsync(
                owner,
                taskA,
                attemptA,
                moduleB,
                "salesforce",
                "Authorize Salesforce",
                materialB,
                parkRevision: 0,
                lifetime: TimeSpan.FromHours(1),
                completer,
                epoch,
                cancellationToken));

        // Same binding identity with divergent custody material must refuse permanently (not return
        // the first surface while storing/returning mismatched payload).
        var divergentMaterial = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await custody.IssueAsync(
                owner,
                taskA,
                attemptA,
                moduleA,
                "google.gmail",
                "Connect Gmail",
                materialB,
                parkRevision: 0,
                lifetime: TimeSpan.FromHours(1),
                completer,
                epoch,
                cancellationToken));
        Assert.Contains(epoch.ToString("N"), divergentMaterial.Message, StringComparison.OrdinalIgnoreCase);

        Assert.True(custody.TryLoadActionMaterial(first.Requirement.ActionReference, out var stored));
        Assert.Equal(materialA, stored);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find DigitalBrain.slnx above {AppContext.BaseDirectory}.");
    }

    private static async Task<T> WaitForAsync<T>(
        Func<Task<T>> read,
        Func<T, bool> ready,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        T? last = default;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            last = await read();
            if (ready(last))
            {
                return last;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }

        throw new TimeoutException($"Condition was not met. Last value: {last}");
    }
}
