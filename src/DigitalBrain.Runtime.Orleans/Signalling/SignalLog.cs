using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Runtime.Orleans;
using DigitalBrain.Runtime.Orleans.Hosting;

namespace DigitalBrain.Runtime.Orleans.Signalling;

/// <summary>Reads a plain neuron's <c>[Signal]</c> records from its brain log and follows them on a session synapse.</summary>
public static class SignalLog
{
    /// <summary>The alias a session yields when history was trimmed past the subscriber's cursor.</summary>
    public const string GapAlias = "synapse.gap";

    /// <summary>Whether <paramref name="signal"/> is a trim gap rather than a published record.</summary>
    public static bool IsGap(SessionSignal signal) => signal.Alias == GapAlias;

    /// <summary>The frozen <c>[Signal]</c> alias of <paramref name="signal"/>.</summary>
    public static string Alias(Type signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return signal.GetCustomAttribute<SignalAttribute>(inherit: false)?.Alias
            ?? throw new ArgumentException($"{signal.Name} is not a [Signal] record.", nameof(signal));
    }

    /// <summary>A <c>[Signal]</c> record from the JSON a session or a journal entry carries.</summary>
    public static T Decode<T>(string json) where T : Signal
        => JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web) ?? throw new JsonException($"A {typeof(T).Name} payload was null.");

    /// <summary><see cref="Decode{T}(string)"/> for a journal payload.</summary>
    public static T Decode<T>(ReadOnlyMemory<byte> json) where T : Signal
        => JsonSerializer.Deserialize<T>(json.Span, JsonSerializerOptions.Web) ?? throw new JsonException($"A {typeof(T).Name} payload was null.");

    /// <summary>The newest held signal of <paramref name="alias"/> that <paramref name="publisher"/> published, or null.</summary>
    public static async Task<SessionSignal?> Newest(IBrainJournalWriter journal, NeuronId publisher, string alias, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        var head = await journal.ReadSince(publisher.Brain.Value, publisher.ToString(), 0, 1, cancellationToken).ConfigureAwait(false);
        if (head.Last <= head.Floor) { return null; }
        var since = Math.Max(head.Floor, head.Last - SignalPage.MaxLimit);
        while (true)
        {
            var page = await journal.ReadSince(publisher.Brain.Value, publisher.ToString(), since, SignalPage.MaxLimit, cancellationToken).ConfigureAwait(false);
            if (page.Signals.LastOrDefault(entry => entry.Type == alias) is { } last) { return ToSignal(last); }
            if (since <= page.Floor) { return null; }
            since = Math.Max(page.Floor, since - SignalPage.MaxLimit);
        }
    }

    /// <summary>Signals of <paramref name="aliases"/> published by <paramref name="source"/> after the subscription is registered.</summary>
    public static async IAsyncEnumerable<SessionSignal> Live(IServiceProvider services, NeuronId source, string[] aliases, string session, Func<Task> onSubscribed,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(onSubscribed);
        var channel = SiloNeurons.Get<ISessionSynapses>(services, SessionSynapses.For(source.Brain, session));
        try
        {
            await channel.Subscribe(source, aliases).WaitAsync(cancellationToken).ConfigureAwait(false);
            await onSubscribed().ConfigureAwait(false);
            long acked = 0;
            while (true)
            {
                SessionBatch? batch;
                try { batch = await channel.Next(acked, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false); }
                catch (TimeoutException) when (!cancellationToken.IsCancellationRequested) { continue; }
                if (batch is null) { continue; }
                foreach (var entry in batch.Signals) { yield return entry; }
                acked = batch.Number;
            }
        }
        finally
        {
            await channel.End().ConfigureAwait(false);
        }
    }

    /// <summary>A journal entry as the session's signal shape. The alias is the journal type.</summary>
    public static SessionSignal ToSignal(JournalSignalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var document = JsonDocument.Parse(entry.Json);
        return new SessionSignal(entry.Publisher, entry.Sequence, entry.Type, document.RootElement.Clone(), entry.At);
    }
}
