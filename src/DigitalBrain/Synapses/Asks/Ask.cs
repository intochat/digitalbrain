using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;

namespace DigitalBrain;

/// <summary>One thing a unit asks the kernel to do. Data only.</summary>
/// <param name="Slot">The <c>[Slot]</c> parameter name the person approved and bound.</param>
/// <param name="Operation">The method name on the slot's capability contract, such as <c>GetJson</c>.</param>
/// <param name="Arguments">A JSON object keyed by the method's parameter names. References only, never file bytes.</param>
/// <param name="Key">The correlation key the unit chooses, returned on the matching <see cref="Answer"/>.</param>
public sealed record Ask(string Slot, string Operation, JsonElement Arguments, string Key)
{
    /// <summary>True when running the ask twice is harmless; only repeatable asks are retried.</summary>
    public bool Repeatable { get; init; }

    /// <summary>The longest one attempt may take; <see langword="null"/> uses <see cref="AskLimits.DefaultTimeout"/>, clamped to <see cref="AskLimits.MaxTimeout"/>.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>True when long-running out-of-turn capability work automatically renews its execution lease while active.</summary>
    public bool KeepAlive { get; init; }
}

/// <summary>A deferred <c>[Slot]</c> handle a unit takes in place of the capability contract: it builds <see cref="Ask"/> values and cannot call anything.</summary>
/// <typeparam name="TContract">The capability contract interface.</typeparam>
/// <param name="Slot">The bound slot name.</param>
public readonly record struct Asks<TContract>(string Slot) where TContract : class
{
    /// <summary>Builds an <see cref="Ask"/> for <paramref name="operation"/> on this slot.</summary>
    /// <param name="operation">The method name on <typeparamref name="TContract"/>.</param>
    /// <param name="key">The correlation key for the ask.</param>
    /// <param name="arguments">A JSON object keyed by the method's parameter names.</param>
    /// <returns>The ask value.</returns>
    public Ask Call(string operation, string key, JsonElement arguments) => new(Slot, operation, arguments, key);
}

/// <summary>The outcome status of an <see cref="Answer"/>.</summary>
public enum AnswerStatus
{
    /// <summary>The capability call succeeded and its JSON result fits within <see cref="AskLimits.MaxResultBytes"/>.</summary>
    Ok,

    /// <summary>The capability call failed (after any permitted retries) or returned a result larger than <see cref="AskLimits.MaxResultBytes"/>.</summary>
    Failed,

    /// <summary>The kernel refused the ask before calling the capability (such as a duplicate open key, dropped slot, or exceeded budget).</summary>
    Refused,

    /// <summary>The ask remained unanswered past <see cref="AskLimits.MaxAge"/>.</summary>
    Expired,

    /// <summary>The ask was cancelled before completion.</summary>
    Cancelled,

    /// <summary>A non-repeatable ask timed out or lost its worker while in flight, so whether it took effect is unknown.</summary>
    Unknown,
}

/// <summary>The settled answer to one <see cref="Ask"/>.</summary>
/// <param name="Slot">The slot name from the ask.</param>
/// <param name="Operation">The contract operation name from the ask.</param>
/// <param name="Key">The correlation key from the ask.</param>
/// <param name="Status">How the ask settled.</param>
/// <param name="Result">The capability's JSON return value when <paramref name="Status"/> is <see cref="AnswerStatus.Ok"/>; default otherwise.</param>
/// <param name="Error">The failure or refusal reason when <paramref name="Status"/> is not <see cref="AnswerStatus.Ok"/>; <see langword="null"/> otherwise.</param>
/// <param name="Remaining">The number of asks of the unit still open after this answer.</param>
/// <param name="Detail">Safe diagnostic detail describing why the ask failed or settled.</param>
public sealed record Answer(
    string Slot,
    string Operation,
    string Key,
    AnswerStatus Status,
    JsonElement Result,
    string? Error,
    int Remaining,
    string? Detail = null)
{
    /// <summary>True when <see cref="Status"/> is <see cref="AnswerStatus.Ok"/>.</summary>
    public bool Ok => Status == AnswerStatus.Ok;
}

/// <summary>A group of settled answers to a unit's asks, journaled by the unit's asks neuron.</summary>
/// <param name="Unit">The state alias of the unit whose asks settled.</param>
/// <param name="Answers">The settled answers in this group.</param>
[Signal("unit.answered")]
public sealed record Answered(string Unit, IReadOnlyList<Answer> Answers) : Signal;

/// <summary>Ephemeral live status reported while an <see cref="Ask"/> is running, delivered to followers without appending to <c>BrainLog</c>.</summary>
/// <param name="Unit">The state alias of the unit whose ask is running.</param>
/// <param name="RunId">The correlation identifier of the running ask attempt.</param>
/// <param name="Key">The ask's correlation key.</param>
/// <param name="Status">Human-readable live activity step.</param>
/// <param name="Fraction">Optional completion fraction in <c>[0.0, 1.0]</c>.</param>
[Signal("unit.ask.progressed")]
[EphemeralSignal]
public sealed record AskProgressed(string Unit, string RunId, string Key, string Status, double? Fraction = null) : Signal;

/// <summary>Lightweight ephemeral progress channel and follower registry for in-flight asks.</summary>
public interface IAskProgress
{
    /// <summary>Reports ephemeral progress for <paramref name="runId"/> without writing to durable storage, renewing its execution lease when active.</summary>
    /// <param name="runId">The active ask run identifier (or correlation key).</param>
    /// <param name="status">Short status text describing current work.</param>
    /// <param name="fraction">Optional completion fraction in <c>[0.0, 1.0]</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask ReportProgress(string runId, string status, double? fraction = null, CancellationToken ct = default);

    /// <summary>Subscribes <paramref name="follower"/> to ephemeral <see cref="AskProgressed"/> updates, optionally filtered by <paramref name="unit"/> or <paramref name="runId"/>.</summary>
    /// <param name="follower">Callback invoked synchronously in memory when a matching update arrives.</param>
    /// <param name="unit">Optional unit state alias filter.</param>
    /// <param name="runId">Optional run identifier or ask correlation key filter.</param>
    /// <returns>A disposable handle that removes the follower.</returns>
    IDisposable Subscribe(Action<AskProgressed> follower, string? unit = null, string? runId = null);
}

/// <summary>Ambient execution handle for the currently running <see cref="Ask"/> attempt, supporting ephemeral progress reporting and lease renewal.</summary>
public sealed class AskRun
{
    private static readonly AsyncLocal<AskRun?> Ambient = new();
    private readonly Func<string, double?, CancellationToken, ValueTask> _report;
    private readonly Func<TimeSpan?, CancellationToken, ValueTask<bool>> _renew;

    internal AskRun(
        string runId,
        string unit,
        string key,
        Func<string, double?, CancellationToken, ValueTask> report,
        Func<TimeSpan?, CancellationToken, ValueTask<bool>> renew)
    {
        RunId = runId;
        Unit = unit;
        Key = key;
        _report = report;
        _renew = renew;
    }

    /// <summary>The <see cref="AskRun"/> bound to the current async execution flow, or <see langword="null"/> outside an ask execution.</summary>
    public static AskRun? Current => Ambient.Value;

    /// <summary>Unique identifier of this running ask attempt (<c>unit/batch/index/attempt</c>).</summary>
    public string RunId { get; }

    /// <summary>The state alias of the unit that issued the ask.</summary>
    public string Unit { get; }

    /// <summary>The ask's correlation key.</summary>
    public string Key { get; }

    /// <summary>Reports ephemeral status on <see cref="Current"/> when present (no-op outside an ask execution) and renews its lease.</summary>
    public static ValueTask Report(string status, double? fraction = null, CancellationToken ct = default) =>
        Ambient.Value is { } run ? run.ReportProgress(status, fraction, ct) : ValueTask.CompletedTask;

    /// <summary>Renews the execution lease of <see cref="Current"/> when present so long-running capability work does not hit a premature timeout.</summary>
    public static ValueTask<bool> Heartbeat(TimeSpan? extension = null, CancellationToken ct = default) =>
        Ambient.Value is { } run ? run.RenewLease(extension, ct) : ValueTask.FromResult(false);

    /// <summary>Reports ephemeral status for this ask run without writing to <c>BrainLog</c>, while renewing its execution lease.</summary>
    public ValueTask ReportProgress(string status, double? fraction = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(status);
        return _report(status, ClampFraction(fraction), ct);
    }

    /// <summary>Renews this ask run's execution lease by <paramref name="extension"/> (or its configured attempt timeout when <see langword="null"/>).</summary>
    public ValueTask<bool> RenewLease(TimeSpan? extension = null, CancellationToken ct = default) =>
        _renew(extension, ct);

    internal static IDisposable Enter(AskRun run)
    {
        var previous = Ambient.Value;
        Ambient.Value = run;
        return new Scope(previous);
    }

    internal static double? ClampFraction(double? fraction) =>
        fraction is { } value
            ? double.IsNaN(value) || double.IsInfinity(value) ? null : Math.Clamp(value, 0.0, 1.0)
            : null;

    private sealed class Scope(AskRun? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Ambient.Value = previous;
            }
        }
    }
}

/// <summary>In-memory implementation of <see cref="IAskProgress"/> with sub-250ms duplicate-tick coalescing and zero durable journal writes.</summary>
public sealed class AskProgressHub : IAskProgress
{
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<long, Subscription> _followers = new();
    private readonly ConcurrentDictionary<string, ActiveRun> _runs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ThrottleEntry> _throttles = new(StringComparer.Ordinal);
    private long _nextFollowerId;

    /// <summary>Creates a new <see cref="AskProgressHub"/> using <paramref name="time"/> (or <see cref="TimeProvider.System"/> when <see langword="null"/>).</summary>
    public AskProgressHub(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <inheritdoc />
    public async ValueTask ReportProgress(string runId, string status, double? fraction = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(status);
        ct.ThrowIfCancellationRequested();

        var clamped = AskRun.ClampFraction(fraction);
        if (_runs.TryGetValue(runId, out var active))
        {
            await active.OnReport(status, clamped, ct).ConfigureAwait(false);
            return;
        }

        Dispatch(new AskProgressed(string.Empty, runId, runId, status, clamped));
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Action<AskProgressed> follower, string? unit = null, string? runId = null)
    {
        ArgumentNullException.ThrowIfNull(follower);
        var id = Interlocked.Increment(ref _nextFollowerId);
        var sub = new Subscription(this, id, follower, unit, runId);
        _followers[id] = sub;
        return sub;
    }

    internal IDisposable RegisterRun(
        string runId,
        string unit,
        string key,
        Func<string, double?, CancellationToken, ValueTask> onReport)
    {
        var active = new ActiveRun(runId, unit, key, onReport);
        _runs[runId] = active;
        var aliasKey = $"{unit}/{key}";
        _runs.TryAdd(aliasKey, active);
        _runs.TryAdd(key, active);
        return new RunRegistration(this, runId, aliasKey, key, active);
    }

    internal bool Dispatch(AskProgressed progress)
    {
        var now = _time.GetUtcNow();
        var throttleKey = string.IsNullOrEmpty(progress.Unit)
            ? progress.RunId
            : $"{progress.Unit}|{progress.RunId}";

        // Coalesce identical rapid sub-250ms ticks while allowing distinct status transitions or completion (1.0) through immediately.
        if (_throttles.TryGetValue(throttleKey, out var last)
            && progress.Fraction != 1.0
            && string.Equals(last.Status, progress.Status, StringComparison.Ordinal)
            && Nullable.Equals(last.Fraction, progress.Fraction)
            && now - last.At < AskLimits.ProgressThrottle)
        {
            return false;
        }

        _throttles[throttleKey] = new ThrottleEntry(progress.Status, progress.Fraction, now);
        foreach (var pair in _followers)
        {
            var sub = pair.Value;
            if (sub.Matches(progress))
            {
                sub.Follower(progress);
            }
        }

        return true;
    }

    private void RemoveFollower(long id) => _followers.TryRemove(id, out _);

    private void UnregisterRun(string runId, string aliasKey, string key, ActiveRun active)
    {
        _runs.TryRemove(new KeyValuePair<string, ActiveRun>(runId, active));
        _runs.TryRemove(new KeyValuePair<string, ActiveRun>(aliasKey, active));
        _runs.TryRemove(new KeyValuePair<string, ActiveRun>(key, active));
        var throttleKey = string.IsNullOrEmpty(active.Unit) ? runId : $"{active.Unit}|{runId}";
        _throttles.TryRemove(throttleKey, out _);
    }

    private readonly record struct ThrottleEntry(string Status, double? Fraction, DateTimeOffset At);

    private sealed record ActiveRun(
        string RunId,
        string Unit,
        string Key,
        Func<string, double?, CancellationToken, ValueTask> OnReport);

    private sealed class Subscription(
        AskProgressHub hub,
        long id,
        Action<AskProgressed> follower,
        string? unit,
        string? runId) : IDisposable
    {
        private int _disposed;

        public Action<AskProgressed> Follower => follower;

        public bool Matches(AskProgressed progress) =>
            (unit is null || string.Equals(unit, progress.Unit, StringComparison.Ordinal))
            && (runId is null
                || string.Equals(runId, progress.RunId, StringComparison.Ordinal)
                || string.Equals(runId, progress.Key, StringComparison.Ordinal));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                hub.RemoveFollower(id);
            }
        }
    }

    private sealed class RunRegistration(
        AskProgressHub hub,
        string runId,
        string aliasKey,
        string key,
        ActiveRun active) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                hub.UnregisterRun(runId, aliasKey, key, active);
            }
        }
    }
}

/// <summary>Compile-time limits and timing constants for unit asks.</summary>
public static class AskLimits
{
    /// <summary>Maximum number of asks a single reaction may return.</summary>
    public const int MaxAsksPerReaction = 1000;

    /// <summary>Maximum number of asks running concurrently for one unit.</summary>
    public const int Concurrency = 4;

    /// <summary>Maximum number of asks open at once for one unit.</summary>
    public const int MaxOpenAsks = 2000;

    /// <summary>Maximum number of asks one unit may issue within a one-hour window.</summary>
    public const int MaxAsksPerHour = 2000;

    /// <summary>Maximum UTF-8 byte length of a single ask's <see cref="Ask.Arguments"/>.</summary>
    public const int MaxArgumentsBytes = 16 * 1024;

    /// <summary>Maximum UTF-8 byte length of a single answer's <see cref="Answer.Result"/>.</summary>
    public const int MaxResultBytes = 256 * 1024;

    /// <summary>Maximum attempts for a <see cref="Ask.Repeatable"/> ask.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Maximum number of answers carried by one <see cref="Answered"/> signal.</summary>
    public const int AnswersPerSignal = 25;

    /// <summary>Maximum character length of <see cref="Ask.Key"/>.</summary>
    public const int MaxKeyLength = 128;

    /// <summary>Maximum serialized UTF-8 byte size of one reaction's ask batch.</summary>
    public const int MaxBatchBytes = 1024 * 1024;

    /// <summary>Default timeout of one attempt when <see cref="Ask.Timeout"/> is <see langword="null"/>.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Maximum timeout allowed for one attempt.</summary>
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Maximum delay before buffered answers are flushed as an <see cref="Answered"/> signal.</summary>
    public static readonly TimeSpan AnswerFlush = TimeSpan.FromSeconds(1);

    /// <summary>Age after which an unanswered ask settles as <see cref="AnswerStatus.Expired"/>.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    /// <summary>Minimum interval for identical ephemeral progress ticks before coalescing.</summary>
    public static readonly TimeSpan ProgressThrottle = TimeSpan.FromMilliseconds(250);
}

/// <summary>Async helper extensions for unit asks and invocation.</summary>
public static class AsksExtensions
{
    /// <summary>Invokes an operation on the unit asynchronously.</summary>
    public static async Task<Answer> Invoke(this IUnit unit, string operation, JsonElement arguments, string? key = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(operation);
        var ask = new Ask(string.Empty, operation, arguments, key ?? Guid.NewGuid().ToString("N"));
        return await unit.Handle(ask, ct).ConfigureAwait(false);
    }

    /// <summary>Gets a JSON result from the unit operation.</summary>
    public static async Task<JsonElement> GetJson(this IUnit unit, string operation, JsonElement arguments, string? key = null, CancellationToken ct = default)
    {
        var answer = await unit.Invoke(operation, arguments, key, ct).ConfigureAwait(false);
        if (!answer.Ok)
        {
            throw new InvalidOperationException(answer.Error ?? answer.Detail ?? "GetJson failed");
        }
        return answer.Result;
    }

    /// <summary>Reads text from the unit operation.</summary>
    public static async Task<string> ReadText(this IUnit unit, string operation, JsonElement arguments, string? key = null, CancellationToken ct = default)
    {
        var answer = await unit.Invoke(operation, arguments, key, ct).ConfigureAwait(false);
        if (!answer.Ok)
        {
            throw new InvalidOperationException(answer.Error ?? answer.Detail ?? "ReadText failed");
        }
        return answer.Result.ValueKind == JsonValueKind.String ? answer.Result.GetString()! : answer.Result.GetRawText();
    }

    /// <summary>Writes text to the unit operation.</summary>
    public static async Task<Answer> WriteText(this IUnit unit, string operation, string text, string? key = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(text);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("text"u8, text);
            writer.WriteEndObject();
            writer.Flush();
        }
        return await unit.Invoke(operation, JsonElement.Parse(buffer.WrittenSpan), key, ct).ConfigureAwait(false);
    }
}
