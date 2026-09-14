using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Core;
using DigitalBrain.Microsoft;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Coding;

[GrainType(CodingVocabulary.SlotType)]
internal sealed class SlotNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<SlotState>> state,
    ISlotBuilder builder,
    IAspireResourceCommands aspire,
    IActiveSlotLease lease,
    SlotOptions options)
    : Neuron<SlotState>(runtime, state), ISlot
{
    private SlotState Current => State ?? SlotState.Idle;

    private bool HoldsLease => lease.HoldsLease && string.Equals(lease.Slot, Id.Name, StringComparison.OrdinalIgnoreCase);

    private static string KnownSlots => string.Join(" or ", SlotOptions.Names.Select(name => $"'{name}'"));

    public Task<Accepted<SlotReceipt>> Build(BuildSlot command) => ExecuteCommandAsync(
        Descriptor("build"), command, CodingJson.Default.BuildSlot, CodingJson.Default.AcceptedSlotReceipt, arguments =>
        {
            RejectUnknownSlot(arguments.Id);
            RejectWhenBusy(arguments.Id);
            if (HoldsLease)
            {
                // Windows cannot overwrite the assemblies this process has loaded (R5.4).
                throw new CommandRejectedException(arguments.Id, $"slot '{Id.Name}' is serving traffic",
                    $"Build the standby slot instead; '{Id.Name}' is serving traffic.");
            }

            var work = Schedule(Signal.FromJson(CodingVocabulary.SlotBuilding,
                new SlotBuildingBody(arguments.Generation, arguments.ChangedFiles ?? []), CodingJson.Default.SlotBuildingBody));
            return new Accepted<SlotReceipt>(Receipt(), work);
        });

    public Task<Accepted<SlotReceipt>> Promote(PromoteSlot command) => ExecuteCommandAsync(
        Descriptor("promote"), command, CodingJson.Default.PromoteSlot, CodingJson.Default.AcceptedSlotReceipt, arguments =>
        {
            RejectUnknownSlot(arguments.Id);
            RejectWhenBusy(arguments.Id);
            if (HoldsLease)
            {
                throw new CommandRejectedException(arguments.Id, $"slot '{Id.Name}' already holds the active lease",
                    $"Slot '{Id.Name}' already holds the active lease; read the slot, there is nothing to promote.");
            }

            // A slot that failed has artifacts and a process nobody can account for, whether the build or
            // a half-done switch was what failed. Rolling back to the previous slot still works: that one
            // is Idle or Retired, never Failed.
            if (Current.Phase == SlotPhase.Failed)
            {
                throw new CommandRejectedException(arguments.Id, $"slot '{Id.Name}' failed",
                    $"Slot '{Id.Name}' failed its last build; build the slot again first.");
            }

            // The slot this silo runs is the one that drains and, for a forward-only landing, stops.
            var work = Schedule(Signal.FromJson(CodingVocabulary.SlotPromoting,
                new SlotPromotionBody(lease.Slot, 0), CodingJson.Default.SlotPromotionBody));
            return new Accepted<SlotReceipt>(Receipt(), work);
        });

    public Task<Accepted<SlotReceipt>> Retire(RetireSlot command) => ExecuteCommandAsync(
        Descriptor("retire"), command, CodingJson.Default.RetireSlot, CodingJson.Default.AcceptedSlotReceipt, arguments =>
        {
            RejectUnknownSlot(arguments.Id);
            RejectWhenBusy(arguments.Id);
            if (HoldsLease)
            {
                throw new CommandRejectedException(arguments.Id, $"slot '{Id.Name}' holds the active lease",
                    $"Slot '{Id.Name}' holds the active lease; promote the other slot first, the live slot is never retired.");
            }

            var work = Schedule(Signal.Create(CodingVocabulary.SlotRetiring, "{}"));
            return new Accepted<SlotReceipt>(Receipt(), work);
        });

    [ReadOnly]
    public Task<SlotSnapshot> Read()
    {
        var current = Current;
        return Task.FromResult(new SlotSnapshot(Id.Name, current.Phase, current.Generation, current.ArtifactsPath,
            current.Healthy, HoldsLease, current.Errors, current.Detail, current.RollbackAllowed, current.Revision));
    }

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var current = Current;
        switch (delivery.Signal.Type)
        {
            case CodingVocabulary.SlotBuilding:
                {
                    if (Body(delivery, CodingJson.Default.SlotBuildingBody) is not { } body)
                    {
                        return;
                    }

                    SlotState next;
                    try
                    {
                        var result = await builder.BuildAsync(Id.Name, body.ChangedFiles, cancellationToken).ConfigureAwait(true);
                        next = result.Build.Succeeded
                            ? current with
                            {
                                Phase = SlotPhase.Built,
                                Generation = body.Generation,
                                ArtifactsPath = result.ArtifactsPath,
                                Errors = [],
                                Detail = result.Build.Detail,
                                RollbackAllowed = !result.TouchesSerializedState,
                                Healthy = false,
                            }
                            : current with
                            {
                                Phase = SlotPhase.Failed,
                                Generation = body.Generation,
                                ArtifactsPath = result.ArtifactsPath,
                                Errors = result.Build.Errors,
                                Detail = $"{result.Build.Detail ?? $"the build left {result.Build.Errors.Count} error(s)"}; the live slot is untouched",
                                Healthy = false,
                            };
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Not this reaction's token: the workspace or the rollback scan gave up on a
                        // deadline of its own.
                        next = current with { Phase = SlotPhase.Failed, Detail = $"the build of slot '{Id.Name}' did not finish; the live slot is untouched" };
                    }
#pragma warning disable CA1031 // a missing dotnet, an unusable artifacts path or Roslyn itself must settle as advice: an escape here is discarded and retried forever, and the slot never settles
                    catch (Exception error) when (error is not OperationCanceledException)
#pragma warning restore CA1031
                    {
                        // ProcessRunner starts dotnet outside its own guard, so a missing dotnet arrives as
                        // a Win32Exception; the workspace and the [GenerateSerializer] scan can throw
                        // anything Roslyn throws. Either way the message is the advice.
                        next = current with { Phase = SlotPhase.Failed, Detail = error.Message + "; the live slot is untouched" };
                    }

                    await SaveAsync(Settle(current, next), cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.SlotPromoting:
                {
                    if (Body(delivery, CodingJson.Default.SlotPromotionBody) is not { } body)
                    {
                        return;
                    }

                    await PromotingAsync(current, body, cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.SlotSwitching:
                {
                    if (Body(delivery, CodingJson.Default.SlotPromotionBody) is not { } body)
                    {
                        return;
                    }

                    await SwitchingAsync(current, body, cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.SlotRetiring:
                {
                    var failure = await StopFailureAsync(Id.Name, cancellationToken).ConfigureAwait(true);
                    // A slot whose resource would not stop is not retired, whatever the command asked for.
                    await SaveAsync(Settle(current, failure is null
                        ? current with { Phase = SlotPhase.Retired, Healthy = false, Detail = null }
                        : current with { Phase = SlotPhase.Failed, Healthy = false, Detail = failure }),
                        cancellationToken).ConfigureAwait(true);
                    break;
                }
            default:
                return;
        }
    }

    private async Task PromotingAsync(SlotState current, SlotPromotionBody body, CancellationToken cancellationToken)
    {
        var endpoints = ResolveEndpoints();
        var url = options.UrlFor(Id.Name);
        var healthy = await endpoints.HealthyAsync(url, cancellationToken).ConfigureAwait(true);
        if (!healthy && body.Attempt == 0)
        {
            // A stopped standby needs starting; one that already answers (a rollback) is left alone.
            try
            {
                await aspire.ExecuteAsync(options.ResourceFor(Id.Name), "start", cancellationToken).ConfigureAwait(true);
            }
            catch (InvalidOperationException error)
            {
                await FailedAsync(current, error.Message, cancellationToken).ConfigureAwait(true);
                return;
            }

            Schedule(Signal.FromJson(CodingVocabulary.SlotPromoting, body with { Attempt = 1 }, CodingJson.Default.SlotPromotionBody));
            await SaveAsync(Settle(current, current with
            {
                Phase = SlotPhase.Starting,
                Healthy = false,
                Detail = "starting " + options.ResourceFor(Id.Name),
            }), cancellationToken).ConfigureAwait(true);
            return;
        }

        if (!healthy)
        {
            // Attempt 0 probed too, and it is the attempt that started the resource, so the count of
            // probes made is one more than the attempt number.
            var probes = body.Attempt + 1;
            if (probes >= options.HealthAttempts)
            {
                await FailedAsync(current, $"slot '{Id.Name}' did not answer /health at {url} after {probes} attempts", cancellationToken).ConfigureAwait(true);
                return;
            }

            // Reads queue behind this bounded wait, so it stays short: the next reaction looks again.
            await Task.Delay(options.HealthPoll, cancellationToken).ConfigureAwait(true);
            Schedule(Signal.FromJson(CodingVocabulary.SlotPromoting, body with { Attempt = body.Attempt + 1 }, CodingJson.Default.SlotPromotionBody));
            return;
        }

        // The standby names the slot it was configured as. A silo answering for a different name can never
        // report this slot's lease, so it is refused here as configuration advice rather than becoming a
        // settle timeout after the lease has already moved. An answer of null is a gated or silent route,
        // which is no evidence either way, and leaves the promotion to its wait.
        if (await endpoints.NamedSlotAsync(url, Id.Name, cancellationToken).ConfigureAwait(true) is { } named
            && !string.Equals(named, Id.Name, StringComparison.OrdinalIgnoreCase))
        {
            var identity = named.Length == 0 ? "carries no slot name at all" : $"names itself '{named}'";
            await FailedAsync(current with { Healthy = true },
                $"slot '{Id.Name}' cannot be promoted: the silo at {url} {identity}, so it will never report '{Id.Name}'s lease. Set '{ActiveSlotNames.SlotKey}' to '{Id.Name}' on that silo.",
                cancellationToken).ConfigureAwait(true);
            return;
        }

        // One read-only read against the fenced standby: it proves the new slot activated its brain and
        // wrote nothing (the fence lets a standby answer exactly this).
        if (await endpoints.SmokeAsync(url, options.SmokePath, cancellationToken).ConfigureAwait(true) is { } failure)
        {
            await FailedAsync(current with { Healthy = true }, failure, cancellationToken).ConfigureAwait(true);
            return;
        }

        Schedule(Signal.FromJson(CodingVocabulary.SlotSwitching, body, CodingJson.Default.SlotPromotionBody));
        await SaveAsync(Settle(current, current with { Phase = SlotPhase.Smoking, Healthy = true, Detail = null }), cancellationToken).ConfigureAwait(true);
    }

    // Everything after the flip happens in this one turn on purpose: the moment the lease moves, this silo
    // is the standby, so nothing it schedules would ever drain here (design 4.4 and the fence).
    private async Task SwitchingAsync(SlotState current, SlotPromotionBody body, CancellationToken cancellationToken)
    {
        if (!await lease.TryAcquireAsync(Id.Name, cancellationToken).ConfigureAwait(true))
        {
            await FailedAsync(current, $"another silo changed the active-slot lease while promoting '{Id.Name}'; nothing was switched", cancellationToken).ConfigureAwait(true);
            return;
        }

        var endpoints = ResolveEndpoints();
        try
        {
            await SwitchTrafficAsync(current, body, endpoints, cancellationToken).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // the lease has already moved, so this silo will never drain a retry: an escape would strand the promotion at Smoking with traffic on a fenced slot
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            await HandBackAndFailAsync(current, body.FromSlot, error.Message, cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task SwitchTrafficAsync(SlotState current, SlotPromotionBody body, ISlotEndpoints endpoints, CancellationToken cancellationToken)
    {
        // The flip is a row; the new slot learns of it through its own refresher. Moving traffic before it
        // does would meet a silo that still refuses every mutation, so wait for the standby to say so.
        if (!await SettledAsync(endpoints, cancellationToken).ConfigureAwait(true))
        {
            await HandBackAndFailAsync(current, body.FromSlot,
                $"slot '{Id.Name}' did not report the active lease within {options.LeaseSettle.TotalSeconds:0}s and nothing was switched",
                cancellationToken).ConfigureAwait(true);
            return;
        }

        // A bounded retry inside this turn, never a scheduled follow-up: the lease has already moved, so
        // anything scheduled here would wait for a drain this silo will not do.
        using var switching = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        switching.CancelAfter(options.SwitchDeadline);
        string? refusal;
        try
        {
            refusal = await SwitchGatewayAsync(endpoints, switching.Token).ConfigureAwait(true);
        }
        catch (InvalidOperationException error)
        {
            refusal = error.Message;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            refusal = $"the gateway at {options.GatewayUrl} did not accept the switch to '{Id.Name}' within {options.SwitchDeadline.TotalSeconds:0}s";
        }

        if (refusal is not null)
        {
            // Traffic never moved, so the lease goes back to the slot that is still serving it.
            await HandBackAndFailAsync(current, body.FromSlot, refusal, cancellationToken).ConfigureAwait(true);
            return;
        }

        // The retiring slot finishes the HTTP it already accepted; YARP keeps in-flight requests on the
        // snapshot they were routed with (spike S4).
        if (options.Grace > TimeSpan.Zero)
        {
            await Task.Delay(options.Grace, cancellationToken).ConfigureAwait(true);
        }

        var confirmed = await endpoints.ActiveAsync(options.GatewayUrl, cancellationToken).ConfigureAwait(true);
        if (confirmed is { } serving && !string.Equals(serving, Id.Name, StringComparison.OrdinalIgnoreCase))
        {
            // The switch was accepted and the gateway is still serving someone else, so traffic never
            // moved: the lease belongs back where the traffic is, not on a slot nothing routes to.
            await HandBackAndFailAsync(current, body.FromSlot,
                $"the gateway accepted the switch but reports '{serving}' as active, not '{Id.Name}'",
                cancellationToken).ConfigureAwait(true);
            return;
        }

        var forwardOnly = !current.RollbackAllowed;
        var landing = body.FromSlot.Length == 0
            ? null
            : forwardOnly
                ? $"slot '{body.FromSlot}' is stopped: this landing changed persisted state, so it is forward-only"
                : $"slot '{body.FromSlot}' is fenced and still running; promote it back to roll back, or retire it";
        // A gateway that did not answer /active at all is not evidence that traffic stayed put, and handing
        // the lease back would leave the gateway pointing at a slot that has just become the standby. The
        // switch stands, and the snapshot says it was never confirmed.
        var detail = confirmed is null
            ? $"the gateway at {options.GatewayUrl} accepted the switch but did not say which slot it serves, so the switch is unconfirmed. {landing}".TrimEnd()
            : landing;
        await SaveAsync(Settle(current, current with { Phase = SlotPhase.Live, Healthy = true, Detail = detail }), cancellationToken).ConfigureAwait(true);

        // Last, and only for a forward-only landing: stopping the retired slot kills the very process this
        // reaction runs in, so the snapshot must be durable first. A crash here leaves the old slot running
        // behind the fence, which is safe, and `retire` stops it.
        if (forwardOnly && body.FromSlot.Length > 0)
        {
            await StopFailureAsync(body.FromSlot, cancellationToken).ConfigureAwait(true);
        }
    }

    // Null when the gateway accepted the switch; otherwise why it never did. The gateway rebuilds its whole
    // route table per update and debounces, so a Retry-After is waited out rather than failed.
    private async Task<string?> SwitchGatewayAsync(ISlotEndpoints endpoints, CancellationToken deadline)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (await endpoints.SwitchAsync(options.GatewayUrl, Id.Name, deadline).ConfigureAwait(true) is not { } wait)
            {
                return null;
            }

            if (attempt >= options.SwitchAttempts)
            {
                return $"the gateway at {options.GatewayUrl} is still debouncing switches after {attempt} attempts";
            }

            await Task.Delay(wait < options.SwitchWait ? wait : options.SwitchWait, deadline).ConfigureAwait(true);
        }
    }

    // Poll the standby's own read, at the interval its refresher runs on, until it agrees it holds the
    // lease. The first probe usually answers false: the row was written moments ago. Bounded by elapsed
    // time rather than by the sum of the polls, because one slow answer would outlast the window alone.
    private async Task<bool> SettledAsync(ISlotEndpoints endpoints, CancellationToken cancellationToken)
    {
        var url = options.UrlFor(Id.Name);
        using var settling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        settling.CancelAfter(options.LeaseSettle);
        try
        {
            while (true)
            {
                if (await endpoints.HoldsLeaseAsync(url, Id.Name, settling.Token).ConfigureAwait(true))
                {
                    return true;
                }

                await Task.Delay(ActiveSlotNames.RefreshInterval, settling.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    // ISlotEndpoints is a typed HttpClient and so transient; an activation lives far longer than one
    // client should, so every reaction resolves its own instead of holding one for the activation's life.
    private ISlotEndpoints ResolveEndpoints() => ServiceProvider.GetRequiredService<ISlotEndpoints>();

    // Every post-flip failure says the same two things: what went wrong, and where the lease ended up.
    private async Task HandBackAndFailAsync(SlotState current, string fromSlot, string what, CancellationToken cancellationToken)
    {
        var handedBack = await HandBackAsync(fromSlot, cancellationToken).ConfigureAwait(true);
        var whereTheLeaseIs = handedBack
            ? $"the lease is back with '{fromSlot}'"
            : fromSlot.Length == 0
                ? $"the lease is on '{Id.Name}' and this silo carries no slot name to hand it back to; set '{ActiveSlotNames.SlotKey}'"
                : $"the lease could not be handed back and is on '{Id.Name}'; promote '{fromSlot}' or repair the lease row";
        await FailedAsync(current, $"{what}; {whereTheLeaseIs}", cancellationToken).ConfigureAwait(true);
    }

    // The lease row is remote, so handing it back can lose its compare-and-swap or fail outright. Either
    // way the detail has to say where the lease actually is instead of claiming the old slot has it.
    private async Task<bool> HandBackAsync(string slot, CancellationToken cancellationToken)
    {
        if (slot.Length == 0)
        {
            return false;
        }

        try
        {
            return await lease.TryAcquireAsync(slot, cancellationToken).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // the hand-back is best effort and its failure is reported in the detail, never thrown over the failure that caused it
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            ServiceProvider.GetService<ILogger<SlotNeuron>>()?.LogWarning(error, "Handing the active-slot lease back to {Slot} failed.", slot);
            return false;
        }
    }

    // Null when the resource stopped; otherwise why it did not.
    private async Task<string?> StopFailureAsync(string slot, CancellationToken cancellationToken)
    {
        try
        {
            await aspire.ExecuteAsync(options.ResourceFor(slot), "stop", cancellationToken).ConfigureAwait(true);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The stop killed this process, which is the point of it; there is nothing left to record.
            return null;
        }
#pragma warning disable CA1031 // a stop that fails is reported in the detail: it must never replace the settle the caller is waiting for, nor escape after the Live snapshot is durable
        catch (Exception error)
#pragma warning restore CA1031
        {
            ServiceProvider.GetService<ILogger<SlotNeuron>>()?.LogWarning(error, "Stopping slot {Slot} failed.", slot);
            return error.Message;
        }
    }

    private Task FailedAsync(SlotState current, string detail, CancellationToken cancellationToken)
        => SaveAsync(Settle(current, current with { Phase = SlotPhase.Failed, Detail = detail }), cancellationToken);

    // The one place a revision is bumped: every reaction saves exactly once, through here.
    private static SlotState Settle(SlotState current, SlotState next) => next with { Revision = current.Revision + 1 };

    private SlotReceipt Receipt() => new(Id.Name, Current.Phase, Current.Revision);

    private void RejectUnknownSlot(CommandId id)
    {
        if (!SlotOptions.Names.Contains(Id.Name, StringComparer.OrdinalIgnoreCase))
        {
            throw new CommandRejectedException(id, $"'{Id.Name}' is not a slot", $"'{Id.Name}' is not a slot; name the slot {KnownSlots}.");
        }
    }

    private void RejectWhenBusy(CommandId id)
    {
        if (Current.Phase is SlotPhase.Building or SlotPhase.Starting or SlotPhase.Smoking)
        {
            var phase = Current.Phase.ToString().ToLowerInvariant();
            throw new CommandRejectedException(id, $"slot '{Id.Name}' is {phase}",
                $"slot '{Id.Name}' is {phase}; wait for it to settle and read it again.");
        }
    }
}
