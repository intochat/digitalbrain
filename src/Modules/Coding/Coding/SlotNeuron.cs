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
    private const int MaxSwitchAttempts = 3;

    private static readonly TimeSpan HealthPoll = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MaxSwitchWait = TimeSpan.FromSeconds(20);

    private SlotState Current => State ?? SlotState.Idle;

    private bool HoldsLease => lease.HoldsLease && string.Equals(lease.Slot, Id.Name, StringComparison.OrdinalIgnoreCase);

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
                    "Promote the other slot first; the live slot is never retired.");
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
                    catch (InvalidOperationException error)
                    {
                        // The workspace is not ready, or the build could not start: the message is the advice.
                        next = current with { Phase = SlotPhase.Failed, Detail = error.Message + "; the live slot is untouched" };
                    }

                    await SaveAsync(next with { Revision = current.Revision + 1 }, cancellationToken).ConfigureAwait(true);
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
                    var detail = await StopAsync(Id.Name, cancellationToken).ConfigureAwait(true);
                    await SaveAsync(current with { Phase = SlotPhase.Retired, Healthy = false, Detail = detail, Revision = current.Revision + 1 },
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
                await SaveAsync(current with { Phase = SlotPhase.Failed, Detail = error.Message, Revision = current.Revision + 1 },
                    cancellationToken).ConfigureAwait(true);
                return;
            }

            Schedule(Signal.FromJson(CodingVocabulary.SlotPromoting, body with { Attempt = 1 }, CodingJson.Default.SlotPromotionBody));
            await SaveAsync(current with { Phase = SlotPhase.Starting, Healthy = false, Detail = "starting " + options.ResourceFor(Id.Name), Revision = current.Revision + 1 },
                cancellationToken).ConfigureAwait(true);
            return;
        }

        if (!healthy)
        {
            if (body.Attempt >= options.HealthAttempts)
            {
                await SaveAsync(current with { Phase = SlotPhase.Failed, Detail = $"slot '{Id.Name}' did not answer /health at {url} after {body.Attempt} attempts", Revision = current.Revision + 1 },
                    cancellationToken).ConfigureAwait(true);
                return;
            }

            // Reads queue behind this bounded wait, so it stays one second: the next reaction looks again.
            await Task.Delay(HealthPoll, cancellationToken).ConfigureAwait(true);
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
            await SaveAsync(current with
            {
                Phase = SlotPhase.Failed,
                Healthy = true,
                Detail = $"slot '{Id.Name}' cannot be promoted: the silo at {url} {identity}, so it will never report '{Id.Name}'s lease. Set '{ActiveSlotNames.SlotKey}' to '{Id.Name}' on that silo.",
                Revision = current.Revision + 1,
            }, cancellationToken).ConfigureAwait(true);
            return;
        }

        // One read-only read against the fenced standby: it proves the new slot activated its brain and
        // wrote nothing (the fence lets a standby answer exactly this).
        if (await endpoints.SmokeAsync(url, options.SmokePath, cancellationToken).ConfigureAwait(true) is { } failure)
        {
            await SaveAsync(current with { Phase = SlotPhase.Failed, Healthy = true, Detail = failure, Revision = current.Revision + 1 },
                cancellationToken).ConfigureAwait(true);
            return;
        }

        Schedule(Signal.FromJson(CodingVocabulary.SlotSwitching, body, CodingJson.Default.SlotPromotionBody));
        await SaveAsync(current with { Phase = SlotPhase.Smoking, Healthy = true, Detail = null, Revision = current.Revision + 1 },
            cancellationToken).ConfigureAwait(true);
    }

    // Everything after the flip happens in this one turn on purpose: the moment the lease moves, this silo
    // is the standby, so nothing it schedules would ever drain here (design 4.4 and the fence).
    private async Task SwitchingAsync(SlotState current, SlotPromotionBody body, CancellationToken cancellationToken)
    {
        if (!await lease.TryAcquireAsync(Id.Name, cancellationToken).ConfigureAwait(true))
        {
            await SaveAsync(current with { Phase = SlotPhase.Failed, Detail = $"another silo changed the active-slot lease while promoting '{Id.Name}'; nothing was switched", Revision = current.Revision + 1 },
                cancellationToken).ConfigureAwait(true);
            return;
        }

        var endpoints = ResolveEndpoints();

        // The flip is a row; the new slot learns of it through its own refresher. Moving traffic before it
        // does would meet a silo that still refuses every mutation, so wait for the standby to say so.
        if (!await SettledAsync(endpoints, cancellationToken).ConfigureAwait(true))
        {
            await HandBackAsync(body.FromSlot, cancellationToken).ConfigureAwait(true);
            await SaveAsync(current with
            {
                Phase = SlotPhase.Failed,
                Detail = $"slot '{Id.Name}' did not report the active lease within {options.LeaseSettle.TotalSeconds:0}s; the lease is back with '{body.FromSlot}' and nothing was switched",
                Revision = current.Revision + 1,
            }, cancellationToken).ConfigureAwait(true);
            return;
        }

        // A bounded retry inside this turn, never a scheduled follow-up: the lease has already moved, so
        // anything scheduled here would wait for a drain this silo will not do.
        for (var attempt = 0; ; attempt++)
        {
            TimeSpan? retryAfter;
            try
            {
                retryAfter = await endpoints.SwitchAsync(options.GatewayUrl, Id.Name, cancellationToken).ConfigureAwait(true);
            }
            catch (InvalidOperationException error)
            {
                // Traffic never moved, so the lease goes back to the slot that is still serving it.
                await HandBackAsync(body.FromSlot, cancellationToken).ConfigureAwait(true);
                await SaveAsync(current with { Phase = SlotPhase.Failed, Detail = error.Message, Revision = current.Revision + 1 },
                    cancellationToken).ConfigureAwait(true);
                return;
            }

            if (retryAfter is not { } wait)
            {
                break;
            }

            if (attempt + 1 >= MaxSwitchAttempts)
            {
                await HandBackAsync(body.FromSlot, cancellationToken).ConfigureAwait(true);
                await SaveAsync(current with
                {
                    Phase = SlotPhase.Failed,
                    Detail = $"the gateway at {options.GatewayUrl} is still debouncing switches after {attempt + 1} attempts; the lease is back with '{body.FromSlot}'",
                    Revision = current.Revision + 1,
                }, cancellationToken).ConfigureAwait(true);
                return;
            }

            await Task.Delay(wait < MaxSwitchWait ? wait : MaxSwitchWait, cancellationToken).ConfigureAwait(true);
        }

        // The retiring slot finishes the HTTP it already accepted; YARP keeps in-flight requests on the
        // snapshot they were routed with (spike S4).
        if (options.Grace > TimeSpan.Zero)
        {
            await Task.Delay(options.Grace, cancellationToken).ConfigureAwait(true);
        }

        var confirmed = await endpoints.ActiveAsync(options.GatewayUrl, cancellationToken).ConfigureAwait(true);
        var forwardOnly = !current.RollbackAllowed;
        var detail = body.FromSlot.Length == 0
            ? null
            : forwardOnly
                ? $"slot '{body.FromSlot}' is stopped: this landing changed persisted state, so it is forward-only"
                : $"slot '{body.FromSlot}' is fenced and still running; promote it back to roll back, or retire it";
        if (!string.Equals(confirmed, Id.Name, StringComparison.OrdinalIgnoreCase))
        {
            detail = $"the gateway reports '{confirmed ?? "unknown"}' as active, not '{Id.Name}'. {detail}";
        }

        await SaveAsync(current with { Phase = SlotPhase.Live, Healthy = true, Detail = detail, Revision = current.Revision + 1 },
            cancellationToken).ConfigureAwait(true);

        // Last, and only for a forward-only landing: stopping the retired slot kills the very process this
        // reaction runs in, so the snapshot must be durable first. A crash here leaves the old slot running
        // behind the fence, which is safe, and `retire` stops it.
        if (forwardOnly && body.FromSlot.Length > 0)
        {
            await StopAsync(body.FromSlot, cancellationToken).ConfigureAwait(true);
        }
    }

    // Poll the standby's own read, at the interval its refresher runs on, until it agrees it holds the
    // lease. The first probe usually answers false: the row was written moments ago.
    private async Task<bool> SettledAsync(ISlotEndpoints endpoints, CancellationToken cancellationToken)
    {
        var url = options.UrlFor(Id.Name);
        var deadline = TimeProvider.GetUtcNow() + options.LeaseSettle;
        while (true)
        {
            if (await endpoints.HoldsLeaseAsync(url, Id.Name, cancellationToken).ConfigureAwait(true))
            {
                return true;
            }

            if (TimeProvider.GetUtcNow() >= deadline)
            {
                return false;
            }

            await Task.Delay(ActiveSlotNames.RefreshInterval, cancellationToken).ConfigureAwait(true);
        }
    }

    // ISlotEndpoints is a typed HttpClient and so transient; an activation lives far longer than one
    // client should, so every reaction resolves its own instead of holding one for the activation's life.
    private ISlotEndpoints ResolveEndpoints() => ServiceProvider.GetRequiredService<ISlotEndpoints>();

    private async Task HandBackAsync(string slot, CancellationToken cancellationToken)
    {
        if (slot.Length > 0)
        {
            await lease.TryAcquireAsync(slot, cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task<string?> StopAsync(string slot, CancellationToken cancellationToken)
    {
        try
        {
            await aspire.ExecuteAsync(options.ResourceFor(slot), "stop", cancellationToken).ConfigureAwait(true);
            return null;
        }
        catch (InvalidOperationException error)
        {
            ServiceProvider.GetService<ILogger<SlotNeuron>>()?.LogWarning(error, "Stopping slot {Slot} failed.", slot);
            return error.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The stop killed this process, which is the point of it; there is nothing left to record.
            return null;
        }
    }

    private SlotReceipt Receipt() => new(Id.Name, Current.Phase, Current.Revision);

    private void RejectUnknownSlot(CommandId id)
    {
        if (!SlotOptions.Names.Contains(Id.Name, StringComparer.OrdinalIgnoreCase))
        {
            throw new CommandRejectedException(id, $"'{Id.Name}' is not a slot", "Name the slot 'a' or 'b'.");
        }
    }

    private void RejectWhenBusy(CommandId id)
    {
        if (Current.Phase is SlotPhase.Building or SlotPhase.Starting or SlotPhase.Smoking)
        {
            throw new CommandRejectedException(id, $"slot '{Id.Name}' is {Current.Phase.ToString().ToLowerInvariant()}",
                "Wait for the slot to settle and read it again.");
        }
    }
}
