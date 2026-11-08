using DigitalBrain.Spi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// <see cref="ISignalHistory"/> of one brain over its BrainLog (04 R4-06): a per-publisher read uses publisher sequences, a
/// brain-wide read uses brain positions. Payloads decode through the closed <see cref="SignalTypeMap"/>. A signal whose type is not
/// a registered alias (an os signal an adapter journaled through <see cref="IBrainJournalWriter"/>, 04-17) is skipped with a logged
/// reason, not an error.
/// </summary>
internal sealed partial class BrainSignalHistory(BrainLogs logs, BrainId brain, ILogger logger) : ISignalHistory
{
    public BrainId Brain { get; } = brain;

    /// <exception cref="UnauthorizedAccessException"><paramref name="publisher"/> belongs to another brain (T-04-13).</exception>
    public async Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct)
        => await Read(publisher, since, limit, keepUnknown: false, ct);

    public async Task<SignalPage> Read(long since, int limit, CancellationToken ct) => await Read(since, limit, keepUnknown: false, ct);

    /// <summary>
    /// The engine's brain-wide read: signals of unknown type stay in the page as <see cref="UnknownSignal"/> with their raw JSON, so an
    /// alias reaction can still hear them and the brain cursor moves past the rest.
    /// </summary>
    internal async Task<SignalPage> Read(long since, int limit, bool keepUnknown, CancellationToken ct)
    {
        SignalPage.CheckLimit(limit);
        var log = await logs.For(Brain.Value);
        var slice = await log.BrainSignals(Math.Max(0, since), limit, ct);
        var page = Page(log.Payloads, slice, null, positions: true);
        return keepUnknown ? page : Without(page);
    }

    public async Task<SignalPage> ReadTrace(string traceId, long since, int limit, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(traceId);
        SignalPage.CheckLimit(limit);
        var log = await logs.For(Brain.Value);
        var slice = await log.TraceSignals(traceId, Math.Max(0, since), limit, ct);
        return Without(Page(log.Payloads, slice, null, positions: true));
    }

    /// <summary>
    /// The engine's read: signals of unknown type stay in the page as <see cref="UnknownSignal"/>, which no subscriber wants, so the
    /// cursor moves past them like any other unwanted signal and a run of them cannot stall catch-up.
    /// </summary>
    internal async Task<SignalPage> Read(NeuronId publisher, long since, int limit, bool keepUnknown, CancellationToken ct)
    {
        SignalPage.CheckLimit(limit);
        if (publisher.Brain != Brain) { throw new UnauthorizedAccessException($"Neuron {publisher} is not in brain {Brain}."); }
        var log = await logs.For(Brain.Value);
        var slice = await log.PublisherSignals(publisher.ToString(), Math.Max(0, since), limit, ct);
        var page = Page(log.Payloads, slice, publisher, positions: false);
        return keepUnknown ? page : Without(page);
    }

    private static SignalPage Without(SignalPage page)
        => page.Signals.Any(item => item.Signal is UnknownSignal) ? page with { Signals = [.. page.Signals.Where(item => item.Signal is not UnknownSignal)] } : page;

    private SignalPage Page(JournalPayloads payloads, StoredSlice slice, NeuronId? publisher, bool positions)
        => new(slice.Floor, slice.Last, [.. slice.Held
            .Where(item => publisher is not null || NeuronId.TryParse(item.Publisher, out _)) // only neurons publish history
            .Select(item => new JournaledSignal(
            publisher ?? NeuronId.Parse(item.Publisher),
            positions ? item.Position : item.Sequence,
            Decode(payloads, item),
            DateTimeOffset.FromUnixTimeMilliseconds(item.AtUnixMs),
            item.Payload.TraceId,
            item.Payload.CausationId))]);

    private Signal Decode(JournalPayloads payloads, StoredSignal item)
    {
        if (payloads.Types.TryResolve(item.Payload.Type, out _)) { return payloads.DecodeSignal(item.Payload); }
        LogUnknownSkipped(logger, item.Payload.Type, item.Publisher, item.Sequence, Brain.Value);
        return new UnknownSignal(item.Payload.Type) { Json = item.Payload.Json };
    }

    [LoggerMessage(EventId = 4130, EventName = "JournaledSignalTypeUnknown", Level = LogLevel.Debug,
        Message = "Signal {Publisher}#{Sequence} in brain {Brain} has type '{Type}', which is not a registered [Signal] alias; it is skipped.")]
    private static partial void LogUnknownSkipped(ILogger logger, string type, string publisher, long sequence, string brain);
}

/// <summary>
/// A journaled signal whose type is not a registered alias. It never leaves the runtime and is delivered only to a state neuron's
/// alias reactions on its alias, which get <see cref="Json"/> as the envelope payload. Equality is by <see cref="Type"/> only.
/// </summary>
internal sealed record UnknownSignal(string Type) : Signal, IUntypedSignal
{
    /// <summary>The payload exactly as journaled; null when the reader did not keep it.</summary>
    public byte[]? Json { get; init; }

    /// <inheritdoc />
    public string Alias => Type;

    /// <inheritdoc />
    public System.Text.Json.JsonElement Payload => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(Json ?? "null"u8.ToArray());

    /// <inheritdoc />
    public bool Equals(UnknownSignal? other) => other is not null && string.Equals(Type, other.Type, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Type);
}

/// <summary>Hands out the <see cref="ISignalHistory"/> of a brain; the host context (04-11) asks it per neuron.</summary>
internal sealed class BrainSignalHistories(BrainLogs logs, ILogger<BrainSignalHistory>? logger = null)
{
    public ISignalHistory For(BrainId brain) => new BrainSignalHistory(logs, brain, logger ?? (ILogger)NullLogger.Instance);
}
