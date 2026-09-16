using System.Text.Json;
using DigitalBrain.Abstractions.Behaviors;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using DigitalBrain.Identity;

namespace DigitalBrain.Core.Behaviors;
/// <summary>A durable lifecycle owner. The existing pending-work journal retries reconciliation.</summary>
[GrainType("behavior")]
public sealed class BehaviorNeuron : Neuron, IBehavior
{
    private const string Saving = "BehaviorSaving";
    private const string Starting = "BehaviorStarting";
    private const string Stopping = "BehaviorStopping";
    private readonly IDurableValue<BehaviorSnapshot> _state;
    private readonly BehaviorCatalog _catalog;
    private readonly IDurableValue<string> _lifecycle;
    public BehaviorNeuron(NeuronRuntime runtime, BehaviorCatalog catalog) : base(runtime)
    {
        _catalog = catalog;
        _lifecycle = ServiceProvider.GetRequiredKeyedService<IDurableValue<string>>("behavior.lifecycle-work");
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<BehaviorSnapshot>>("behavior.definition");
    }

    private BehaviorSnapshot Current => _state.Value ?? new(null, 0, BehaviorStatus.Draft, null, []);

    public async Task<Accepted<BehaviorVersion>> Save(SaveBehavior command)
    {
        await RequireAuthorization(command.Definition).ConfigureAwait(true);
        await ClearAbandonedReservation().ConfigureAwait(true);
        return await ExecuteCommandAsync(Descriptor("save"), command, BehaviorJson.Default.SaveBehavior, BehaviorJson.Default.AcceptedBehaviorVersion, args =>
        {
            RequireVersion(args);
            RequireNoLifecycle(args);
            if (Current.Status is not (BehaviorStatus.Draft or BehaviorStatus.Stopped))
            {
                throw new CommandRejectedException(args.Id, "behavior is active", "Stop and wait for Stopped before changing its definition.");
            }

            var validation = _catalog.Validate(args.Definition);
            if (!validation.Valid)
            {
                throw new CommandRejectedException(args.Id, "invalid behavior", string.Join("; ", validation.Errors));
            }

            var work = Schedule(Signal.FromJson(Saving, args.Definition, BehaviorJson.Default.BehaviorDefinition));
            _lifecycle.Value = work.ToString();
            return new Accepted<BehaviorVersion>(new(Current.Version), work);
        }).ConfigureAwait(true);
    }
    public Task<Accepted<BehaviorVersion>> Start(ChangeBehavior command) => Change(command, "start", Starting);
    public Task<Accepted<BehaviorVersion>> Stop(ChangeBehavior command) => Change(command, "stop", Stopping);
    private async Task<Accepted<BehaviorVersion>> Change(ChangeBehavior command, string method, string type)
    {
        if (type == Starting) { await RequireAuthorization(Current.Definition).ConfigureAwait(true); }
        await ClearAbandonedReservation().ConfigureAwait(true);
        return await ExecuteCommandAsync(Descriptor(method), command, BehaviorJson.Default.ChangeBehavior, BehaviorJson.Default.AcceptedBehaviorVersion, args =>
        {
            RequireVersion(args);
            RequireNoLifecycle(args);
            if (type == Starting)
            {
                if (Current.Definition is null)
                {
                    throw new CommandRejectedException(args.Id, "no definition", "Save a valid definition and wait for its work to complete.");
                }

                var validation = _catalog.Validate(Current.Definition);
                if (!validation.Valid)
                {
                    throw new CommandRejectedException(args.Id, "capabilities changed", string.Join("; ", validation.Errors));
                }
            }

            var work = Schedule(Signal.Create(type, "{}"));
            _lifecycle.Value = work.ToString();
            return new Accepted<BehaviorVersion>(new(Current.Version), work);
        }).ConfigureAwait(true);
    }
    private async Task RequireAuthorization(BehaviorDefinition? definition)
    {
        if (IdentityCallerContext.Current is not { } caller) { return; }
        if (definition?.Authorization is not { } authorization
            || !await ServiceProvider.GetRequiredService<IdentityService>().OwnsAutomationGrantAsync(
                caller.UserId, authorization.GrantId, authorization.WorkspaceId, Id.ToString()).ConfigureAwait(true))
        {
            throw new UnauthorizedAccessException("Behavior authoring requires your active grant for this behavior and workspace.");
        }
    }

    private async Task ClearAbandonedReservation()
    {
        // CancelReaction removes pending work independently of the lifecycle handler. Once the
        // queue is empty, a leftover reservation cannot still have an operation to apply.
        if (!string.IsNullOrEmpty(_lifecycle.Value) && await ReadPendingCount().ConfigureAwait(true) == 0)
        {
            _lifecycle.Value = string.Empty;
            await PersistAsync().ConfigureAwait(true);
        }
    }

    private void RequireNoLifecycle(Command command)
    {
        if (!string.IsNullOrEmpty(_lifecycle.Value))
        {
            throw new CommandRejectedException(command.Id, "lifecycle work pending",
                $"Wait for or cancel pending work {_lifecycle.Value} before another lifecycle change.");
        }
    }
    private void RequireVersion(Command command)
    {
        if (command.ExpectedVersion is { } version && version != Current.Version)
        {
            throw new CommandRejectedException(command.Id, "version changed", "Read the behavior and retry against its current version.");
        }
    }

    public Task<BehaviorSnapshot> Read() => Task.FromResult(Current);
    public Task<BehaviorValidation> Validate(BehaviorDefinition definition) => Task.FromResult(_catalog.Validate(definition));
    public Task<IReadOnlyList<BehaviorCapability>> Catalog() => Task.FromResult(_catalog.All);

    private IBehaviorDirectory Directory => GrainFactory.GetGrain<IBehaviorDirectory>(new NeuronId("behaviors", "default").ToGrainId());

    public Task<IReadOnlyList<NeuronId>> List() => Directory.List();

    public async Task<IReadOnlyList<BehaviorDiagnostic>> Diagnostics()
    {
        var result = new List<BehaviorDiagnostic>();
        foreach (var binding in Current.Bindings.Where(binding => binding.Owned))
        {
            result.Add(new(binding.Role, binding.Neuron, await Processor(binding.Neuron).Status().ConfigureAwait(true)));
        }
        return result;
    }
    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        // Lifecycle commands are scheduled locally. External events may not reconfigure a behavior.
        if (delivery.Source != Id)
        {
            return;
        }

        await ApplyLifecycle(delivery, cancellationToken).ConfigureAwait(true);
        if (_lifecycle.Value == delivery.SignalId.ToString())
        {
            _lifecycle.Value = string.Empty;
            await PersistAsync().ConfigureAwait(true);
        }
    }

    private async Task ApplyLifecycle(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        switch (delivery.Signal.Type)
        {
            case Saving:
                if (Current.Status is not (BehaviorStatus.Draft or BehaviorStatus.Stopped))
                {
                    return;
                }

                var definition = JsonSerializer.Deserialize(delivery.Signal.Body, BehaviorJson.Default.BehaviorDefinition)!;
                await Directory.Register(Id).ConfigureAwait(true);
                // Pending work can replay after the snapshot committed. The operation id makes saves idempotent.
                if (Current.RunId == delivery.SignalId.ToString())
                {
                    return;
                }

                _state.Value = new(definition, Current.Version + 1, BehaviorStatus.Stopped, delivery.SignalId.ToString(), []);
                await PersistAsync().ConfigureAwait(true);
                break;
            case Starting:
                if (Current.Status == BehaviorStatus.Running)
                {
                    return;
                }

                if (Current.Status == BehaviorStatus.Stopping)
                {
                    await StopResources(cancellationToken).ConfigureAwait(true);
                    return;
                }

                if (Current.Definition is null)
                {
                    return;
                }

                if (Current.Status != BehaviorStatus.Starting)
                {
                    var run = delivery.SignalId.ToString();
                    var bindings = Current.Definition.Nodes.Select(node =>
                    {
                        var capability = _catalog.Get(node.Capability);
                        return new BehaviorNodeBinding(node.Role, node.SharedNeuron ?? new NeuronId(capability.GrainType, BehaviorMapping.OwnedName(Id, run, node.Role)), capability.Ownership == BehaviorOwnership.Owned);
                    }).ToArray();
                    _state.Value = Current with
                    {
                        Status = BehaviorStatus.Starting,
                        RunId = run,
                        Bindings = bindings,
                        Error = null
                    };
                    await PersistAsync().ConfigureAwait(true);
                }

                await StartResources(cancellationToken).ConfigureAwait(true);
                break;
            case Stopping:
                if (Current.Status is BehaviorStatus.Stopped or BehaviorStatus.Draft)
                {
                    return;
                }

                _state.Value = Current with
                {
                    Status = BehaviorStatus.Stopping,
                    Error = null
                };
                await PersistAsync().ConfigureAwait(true);
                await StopResources(cancellationToken).ConfigureAwait(true);
                break;
        }
    }

    private string Owner => $"{Id}/{Current.RunId}";

    private INeuronOwnership Ownership(NeuronId id) => GrainFactory.GetGrain<INeuronOwnership>(id.ToGrainId());
    private IBehaviorNode Processor(NeuronId id) => GrainFactory.GetGrain<IBehaviorNode>(id.ToGrainId());
    private async Task StartResources(CancellationToken cancellationToken)
    {
        try
        {
            var definition = Current.Definition!;
            var validation = _catalog.Validate(definition);
            if (!validation.Valid)
            {
                throw new InvalidOperationException($"Capabilities changed: {string.Join("; ", validation.Errors)}");
            }

            var bindings = Current.Bindings.ToDictionary(binding => binding.Role, StringComparer.Ordinal);
            foreach (var node in definition.Nodes.Where(node => bindings[node.Role].Owned))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = bindings[node.Role].Neuron;
                await Ownership(id).Claim(Owner).ConfigureAwait(true);
                await Processor(id).Configure(new(Owner, node, true, Current.Definition!.Authorization,
                    Current.Definition.Authorization is null ? null : Id.ToString())).ConfigureAwait(true);
            }

            // Internal edges first, then source subscriptions: all downstream processing is ready at ingress.
            foreach (var edge in definition.Connections.OrderBy(edge => !bindings[edge.From].Owned))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = definition.Nodes.Single(node => node.Role == edge.From);
                await Ownership(bindings[edge.From].Neuron).ConnectFor(Owner, bindings[edge.To].Neuron, source.Output!.SignalType).ConfigureAwait(true);
            }

            _state.Value = Current with
            {
                Status = BehaviorStatus.Running,
                Error = null
            };
            await PersistAsync().ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Persist cleanup intent before rollback; the queued start retries this cleanup after a crash.
            _state.Value = Current with
            {
                Status = BehaviorStatus.Stopping,
                Error = error.Message
            };
            await PersistAsync().ConfigureAwait(true);
            await StopResources(cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task StopResources(CancellationToken cancellationToken)
    {
        var definition = Current.Definition!;
        var bindings = Current.Bindings.ToDictionary(binding => binding.Role, StringComparer.Ordinal);
        foreach (var edge in definition.Connections.OrderBy(edge => bindings[edge.From].Owned))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = definition.Nodes.Single(node => node.Role == edge.From);
            await Ownership(bindings[edge.From].Neuron).DisconnectFor(Owner, bindings[edge.To].Neuron, source.Output!.SignalType).ConfigureAwait(true);
        }

        foreach (var node in definition.Nodes.Where(node => bindings[node.Role].Owned))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = bindings[node.Role].Neuron;
            await Processor(id).Configure(new(Owner, node, false, Current.Definition!.Authorization,
                Current.Definition.Authorization is null ? null : Id.ToString())).ConfigureAwait(true);
            await Ownership(id).Release(Owner).ConfigureAwait(true);
        }

        _state.Value = Current with
        {
            Status = BehaviorStatus.Stopped
        };
        await PersistAsync().ConfigureAwait(true);
    }
}


