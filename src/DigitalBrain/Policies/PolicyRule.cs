namespace DigitalBrain;

/// <summary>How a <see cref="PolicyRule"/> measures an action against its limit.</summary>
public enum PolicyMeasure
{
    /// <summary>Counts each matching call as one.</summary>
    Count,

    /// <summary>Sums the amount passed to <see cref="IPolicies.Allow"/> in the action's <see cref="ActionDescriptor.AmountUnit"/>.</summary>
    Sum,
}

/// <summary>The accumulation window of a <see cref="PolicyRule"/>.</summary>
public enum PolicyWindowKind
{
    /// <summary>Applies the limit to each single call without accumulating across calls.</summary>
    None,

    /// <summary>Accumulates over the current UTC day (from 00:00 UTC).</summary>
    Day,

    /// <summary>Accumulates over the current UTC week (from Monday 00:00 UTC).</summary>
    Week,

    /// <summary>Accumulates over the current UTC month (from the 1st at 00:00 UTC).</summary>
    Month,

    /// <summary>Accumulates over a rolling <see cref="TimeSpan"/> ending at the current time.</summary>
    Rolling,
}

/// <summary>The window over which a <see cref="PolicyRule"/> accumulates reservations.</summary>
/// <param name="Kind">The window kind.</param>
/// <param name="Span">The rolling span when <paramref name="Kind"/> is <see cref="PolicyWindowKind.Rolling"/>; null otherwise.</param>
public sealed record PolicyWindow(PolicyWindowKind Kind, TimeSpan? Span);

/// <summary>The string comparison operator of a <see cref="PolicyCondition"/>.</summary>
public enum PolicyOp
{
    /// <summary>Exact ordinal equality.</summary>
    Equals,

    /// <summary>Ordinal substring match.</summary>
    Contains,

    /// <summary>Ordinal prefix match.</summary>
    StartsWith,

    /// <summary>Ordinal suffix match.</summary>
    EndsWith,
}

/// <summary>An optional condition narrowing when a <see cref="PolicyRule"/> applies.</summary>
/// <param name="Fact">The fact key to inspect.</param>
/// <param name="Op">The string operator.</param>
/// <param name="Value">The value compared against the fact.</param>
public sealed record PolicyCondition(string Fact, PolicyOp Op, string Value);

/// <summary>A promise rule constraining a module action.</summary>
/// <param name="Id">The rule id.</param>
/// <param name="Action">The action identifier (<c>{module}.{verb}</c>).</param>
/// <param name="Measure">Whether the rule counts calls or sums amounts.</param>
/// <param name="Window">The accumulation window.</param>
/// <param name="Limit">The non-negative cap within the window.</param>
/// <param name="Condition">The optional narrowing condition, or null when unconditional.</param>
/// <param name="Text">The sentence rendered from the rule.</param>
/// <param name="AdoptedAt">When the rule was created or adopted.</param>
public sealed record PolicyRule(
    string Id,
    string Action,
    PolicyMeasure Measure,
    PolicyWindow Window,
    decimal Limit,
    PolicyCondition? Condition,
    string Text,
    DateTimeOffset AdoptedAt);
