using System.Text.Json;

namespace DigitalBrain.Units;

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
public sealed record Answer(string Slot, string Operation, string Key, AnswerStatus Status, JsonElement Result, string? Error, int Remaining)
{
    /// <summary>True when <see cref="Status"/> is <see cref="AnswerStatus.Ok"/>.</summary>
    public bool Ok => Status == AnswerStatus.Ok;
}

/// <summary>A group of settled answers to a unit's asks, journaled by the unit's asks neuron.</summary>
/// <param name="Unit">The state alias of the unit whose asks settled.</param>
/// <param name="Answers">The settled answers in this group.</param>
[Signal("unit.answered")]
public sealed record Answered(string Unit, IReadOnlyList<Answer> Answers) : Signal;

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
}
