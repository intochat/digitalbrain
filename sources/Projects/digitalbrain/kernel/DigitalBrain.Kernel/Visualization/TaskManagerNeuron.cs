using System.Text.Json;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime.Ui;
using DigitalBrain.Runtime.Visualization;
using Microsoft.Extensions.Options;
using Orleans.Journaling;

namespace DigitalBrain.Kernel.Visualization;

// Kernel-side projection of correlation activity into a TaskManager RFW card.
// Three collaborators:
//   - TaskManagerObserverGrain forwards every timeline synapse via Observe.
//   - TaskManagerTicker (IHostedService) calls Tick on TickInterval; the
//     projection skips the broadcast when JSON has not changed.
//   - CancelCorrelation arrives through the implicit stream subscription on
//     the TaskManagerNeuron type (kept via HandleSynapseAsync), letting any
//     synapse author flag a row as "cancelling" without crossing the cortex.
[ImplicitStreamSubscription(TaskManagerNeuronType)]
public sealed class TaskManagerNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    ITaskManagerBroadcaster broadcaster,
    IOptions<TaskManagerOptions> options,
    TimeProvider time,
    ILogger<TaskManagerNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      ITaskManagerNeuron, INeuronMetadata,
      IHandle<CancelCorrelation>
{
    public const string TaskManagerNeuronType = nameof(TaskManagerNeuron);

    public static NeuronId Id => new("kernel/task-manager");
    public static string Icon => "task-manager";
    public static NeuronCapability Capabilities => NeuronCapability.Reasoning;

    readonly Dictionary<Guid, ActiveTask> _active = new();
    readonly LinkedList<Guid> _lru = new();
    string? _lastSignature;
    int _completed;
    // _failed is currently always 0; the failure-counting path lands when
    // cooperative cancellation arrives (E-RUN scope per the spec §14).
    const int FailedPlaceholder = 0;

    protected override Task HandleSynapseAsync(Synapse synapse) => synapse switch
    {
        CancelCorrelation cancel => OnCancel(cancel),
        _ => Task.CompletedTask,
    };

    Task OnCancel(CancelCorrelation cancel)
    {
        if (_active.TryGetValue(cancel.TargetCorrelationId, out var task))
            task.Status = "cancelling";
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> GetActiveCorrelationIdsAsync()
    {
        IReadOnlyList<Guid> list = _active.Keys.ToArray();
        return Task.FromResult(list);
    }

    public Task Observe(Synapse synapse)
    {
        TaskManagerProjection.Observe(_active, _lru, options.Value.MaxTracked, synapse,
            evictedCallback: t => { Histogram("taskmanager.edges_per_task").Record(t.EdgeCount); _completed++; });
        return Task.CompletedTask;
    }

    public async Task Tick()
    {
        Counter("taskmanager.ticks").Increment(1);
        var now = time.GetUtcNow();
        TaskManagerProjection.Sweep(_active, _lru, options.Value.IdleTimeout, now,
            agedOutCallback: t => { Histogram("taskmanager.edges_per_task").Record(t.EdgeCount); _completed++; });
        var payload = TaskManagerProjection.Project(_active.Values, _completed, FailedPlaceholder, now);
        Counter("taskmanager.active").Increment(payload.Totals.Active);
        var signature = TaskManagerProjection.Signature(payload);
        if (signature == _lastSignature) return;
        _lastSignature = signature;
        var json = JsonSerializer.Serialize(payload);
        await BroadcastAsync(json);
    }

    async Task BroadcastAsync(string dataJson)
    {
        var card = new RfwCard(LibraryName:        "digitalbrain",
        RootWidget:         "TaskManagerCard",
        DataJson:           dataJson) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: Guid.Empty,
            causationId: null,
            callerNeuronId: InstanceId,
            callerNeuronType: TaskManagerNeuronType,
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "HomeFeed",
            timestamp: time.GetUtcNow()
        ) };
        await broadcaster.BroadcastAsync(card);
    }
}
