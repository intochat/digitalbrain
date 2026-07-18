using Brain.Contracts;
using Orleans.Runtime;
using Orleans.Streams;

namespace Brain.Kernel;

public static class NeuronNotificationPublisher
{
    public static string StreamProviderName { get; } = nameof(NeuronNotification);
    public static string StreamNamespace { get; } = nameof(NeuronNotification);

    public static async Task PublishAsync(Grain grain, NeuronNotification notification)
    {
        try
        {
            var provider = grain.GetStreamProvider(StreamProviderName);
            var stream = provider.GetStream<NeuronNotification>(
                StreamId.Create(StreamNamespace, grain.GetPrimaryKeyString()));
            await stream.OnNextAsync(notification);
        }
        catch (Exception exception) when (exception is not BrainException)
        {
            throw new BrainException(
                NeuronFailureKind.ProviderUnavailable,
                exception.Message);
        }
    }
}
