using System.Runtime.CompilerServices;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>Reads the durable synapse records and live deliveries of one brain.</summary>
public static class SynapseObservation
{
    /// <summary>Every synapse stored for <paramref name="brain"/> using the registered journal logs in <paramref name="services"/>.</summary>
    public static Task<ObservedSynapse[]> List(IServiceProvider? services, string brain, CancellationToken cancellationToken = default)
        => services?.GetService<BrainLogs>() is { } logs
            ? List(logs, brain, cancellationToken)
            : Task.FromResult<ObservedSynapse[]>([]);

    /// <summary>Live signal deliveries for <paramref name="brain"/> from the registered delivery hub in <paramref name="services"/>.</summary>
    public static IAsyncEnumerable<SignalDelivery> Deliveries(IServiceProvider? services, string brain, CancellationToken cancellationToken = default)
        => services?.GetService<DeliveryHub>() is { } hub
            ? hub.Listen(brain, cancellationToken)
            : EmptyDeliveries(cancellationToken);

    private static async IAsyncEnumerable<SignalDelivery> EmptyDeliveries([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <summary>The neuron id a host grain id names, or null when <paramref name="grainId"/> is not a neuron host.</summary>
    internal static string? SubscriberOf(string grainId)
    {
        foreach (var type in new[] { NeuronTypeRegistry.AffineGrainType, NeuronTypeRegistry.ReentrantGrainType, NeuronTypeRegistry.StatelessGrainType })
        {
            var prefix = type + "/";
            if (grainId.StartsWith(prefix, StringComparison.Ordinal) && grainId.Length > prefix.Length) { return grainId[prefix.Length..]; }
        }
        return null;
    }

    /// <summary>Every synapse stored for <paramref name="brain"/>.</summary>
    internal static async Task<ObservedSynapse[]> List(BrainLogs logs, string brain, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentException.ThrowIfNullOrWhiteSpace(brain);
        cancellationToken.ThrowIfCancellationRequested();
        var log = await logs.For(brain).WaitAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<ObservedSynapse>();
        foreach (var subs in log.Subscriptions.All)
        {
            var subscriber = subs.Subscriber.ToString();
            foreach (var entry in subs.Entries)
            {
                if (entry.IsBrainWide || entry.Source is null) { continue; }
                var source = entry.Source.Value.ToString();
                foreach (var alias in entry.Aliases)
                {
                    if (alias.Length > 0) { rows.Add(new ObservedSynapse(source, subscriber, alias)); }
                }
            }
        }
        return [.. rows.OrderBy(row => row.From, StringComparer.Ordinal).ThenBy(row => row.To, StringComparer.Ordinal).ThenBy(row => row.Signal, StringComparer.Ordinal)];
    }
}
