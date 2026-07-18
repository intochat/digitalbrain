using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using Orleans.Streams;

namespace DigitalBrain.Os.Infrastructure.Orleans;

public static class SynapseStream
{
    public const string ProviderName = "DigitalBrainTimeline";

    public static IAsyncStream<T> Timeline<T>(this IStreamProvider provider) =>
        provider.GetStream<T>(StreamId.Create("timeline", "global"));

    public static IAsyncStream<Synapse> Timeline(this IStreamProvider provider) =>
        provider.GetStream<Synapse>(StreamId.Create("timeline", "global"));
}
