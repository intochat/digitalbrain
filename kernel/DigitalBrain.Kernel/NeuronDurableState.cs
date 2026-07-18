using DigitalBrain;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Kernel;

public sealed class NeuronDurableState
{
    public NeuronDurableState(
        [FromKeyedServices(nameof(Status))] IDurableValue<NeuronStatus> status,
        [FromKeyedServices(nameof(Operations))] IDurableDictionary<Guid, ExternalOperation> operations,
        [FromKeyedServices(nameof(Outbox))] IDurableDictionary<Guid, NeuronNotification> outbox)
    {
        Status = status;
        Operations = operations;
        Outbox = outbox;
    }

    public IDurableValue<NeuronStatus> Status { get; }
    public IDurableDictionary<Guid, ExternalOperation> Operations { get; }
    public IDurableDictionary<Guid, NeuronNotification> Outbox { get; }
}
