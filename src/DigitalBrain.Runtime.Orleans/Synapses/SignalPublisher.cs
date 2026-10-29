using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Streams;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// The signals of one completed journal append. Only the runtime host context (04-11) constructs it, after the append is
/// durable, so nothing unappended can be announced.
/// </summary>
internal sealed class AppendedSignals
{
    internal AppendedSignals(NeuronId publisher, IReadOnlyList<(long Sequence, string Alias)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Publisher = publisher;
        Entries = entries;
    }

    public NeuronId Publisher { get; }

    public IReadOnlyList<(long Sequence, string Alias)> Entries { get; }
}

/// <summary>
/// The publish half of synapses (RT-02, 04 D-01): after a journal append, one <see cref="SignalReference"/> per signal
/// namespace carrying the highest appended sequence; on publisher activation, the publisher's own last sequence again.
/// Never carries signal content.
/// </summary>
internal sealed partial class SignalPublisher(IServiceProvider services, PublishFaults faults, ILogger<SignalPublisher>? logger = null)
{
    private readonly ILogger _log = logger ?? (ILogger)NullLogger.Instance;

    private IStreamProvider Streams => services.GetRequiredKeyedService<IStreamProvider>(IntoChatOSNames.SignalStreams);

    /// <summary>
    /// Announces a completed append. The append is already durable, so a failed send (a stopping silo, an unreachable queue) does
    /// not fail the save: it is logged and the write-then-crash mechanisms recover it (re-announce on the publisher's activation,
    /// reconcile on silo start). Failing the save instead would make a reaction that saves look failed and run it again.
    /// Only the injected <see cref="PublishFaults"/> fault throws, standing in for a caller killed at this point.
    /// </summary>
    public async Task Publish(AppendedSignals appended)
    {
        ArgumentNullException.ThrowIfNull(appended);
        if (faults.ShouldFail(appended.Publisher)) { throw new InvalidOperationException("PublishFaults.FailAfterAppend: the append is durable, no reference was sent."); }
        var latest = appended.Entries
            .GroupBy(entry => StreamNames.SignalNamespace(entry.Alias), StringComparer.Ordinal)
            .Select(group => (Namespace: group.Key, Sequence: group.Max(entry => entry.Sequence)));
        try { await Send(appended.Publisher, latest).ConfigureAwait(true); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            LogPublishFailed(_log, error, appended.Publisher.ToString());
        }
    }

    /// <summary>Re-announces the publisher's last sequence on activation, once per namespace it publishes.</summary>
    public Task Announce(NeuronId publisher, long last, IEnumerable<string> namespaces)
    {
        ArgumentNullException.ThrowIfNull(namespaces);
        return Send(publisher, namespaces.Distinct(StringComparer.Ordinal).Select(ns => (ns, last)));
    }

    private async Task Send(NeuronId publisher, IEnumerable<(string Namespace, long Sequence)> items)
    {
        var streams = Streams;
        foreach (var (ns, sequence) in items)
        {
            var stream = streams.GetStream<SignalReference>(StreamNames.ForNamespace(publisher.Brain.Value, ns));
            await stream.OnNextAsync(new SignalReference(publisher, sequence));
            if (faults.DuplicateEach) { await stream.OnNextAsync(new SignalReference(publisher, sequence)); }
        }
    }

    [LoggerMessage(EventId = 4123, EventName = "SignalPublishFailed", Level = LogLevel.Warning,
        Message = "The reference of {Publisher}'s durable append was not sent; re-announce and start-up reconcile deliver it.")]
    private static partial void LogPublishFailed(ILogger logger, Exception error, string publisher);
}

/// <summary>Registration for the publisher; the runtime composition (04-11) calls it.</summary>
internal static class SignalPublishing
{
    public static IServiceCollection AddSignalPublisher(this IServiceCollection services)
    {
        services.TryAddSingleton<PublishFaults>();
        services.TryAddSingleton<SignalPublisher>();
        return services;
    }
}
