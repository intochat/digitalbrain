using Orleans.Concurrency;

namespace DigitalBrain;

[Alias("db.neuron-host")]
internal interface INeuronHost : IGrainWithStringKey
{
    [Alias("deliver")]
    Task<DeliveryResult> DeliverAsync<TSynapse>(TSynapse synapse, CancellationToken cancellationToken)
        where TSynapse : Synapse;

    [Alias("publish")]
    Task PublishAsync(Synapse synapse);

    [ReadOnly]
    [Alias("read-journal")]
    Task<JournalRead> ReadAsync(long afterPosition, int maximumRecords);

    [Alias("drain")]
    Task DrainAsync();
}
