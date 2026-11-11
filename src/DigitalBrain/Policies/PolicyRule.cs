namespace DigitalBrain.Limits;

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
public sealed record PolicyWindow(PolicyWindowKind Kind, TimeSpan? Span)
{
    internal static readonly TimeSpan MinimumRollingSpan = TimeSpan.FromHours(1);

    internal bool Contains(DateTimeOffset reservationAt, DateTimeOffset utcNow) => Kind switch
    {
        PolicyWindowKind.None => false,
        PolicyWindowKind.Day => reservationAt >= new DateTimeOffset(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, TimeSpan.Zero),
        PolicyWindowKind.Week => reservationAt >= StartOfUtcWeek(utcNow),
        PolicyWindowKind.Month => reservationAt >= new DateTimeOffset(utcNow.Year, utcNow.Month, 1, 0, 0, 0, TimeSpan.Zero),
        PolicyWindowKind.Rolling => reservationAt > utcNow - (Span ?? TimeSpan.Zero),
        _ => false,
    };

    private static DateTimeOffset StartOfUtcWeek(DateTimeOffset utcNow)
    {
        var diff = ((int)utcNow.DayOfWeek + 6) % 7;
        var monday = utcNow.UtcDateTime.Date.AddDays(-diff);
        return new DateTimeOffset(monday, TimeSpan.Zero);
    }
}

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
public sealed record PolicyCondition(string Fact, PolicyOp Op, string Value)
{
    internal bool Matches(IReadOnlyDictionary<string, string>? facts)
    {
        if (facts is null || !facts.TryGetValue(Fact, out var actual) || actual is null)
        {
            return true;
        }

        return Op switch
        {
            PolicyOp.Equals => string.Equals(actual, Value, StringComparison.Ordinal),
            PolicyOp.Contains => actual.Contains(Value, StringComparison.Ordinal),
            PolicyOp.StartsWith => actual.StartsWith(Value, StringComparison.Ordinal),
            PolicyOp.EndsWith => actual.EndsWith(Value, StringComparison.Ordinal),
            _ => false,
        };
    }
}

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
    DateTimeOffset AdoptedAt)
{
    internal decimal IncrementFor(decimal amount) => Measure == PolicyMeasure.Count ? 1m : amount;

    internal bool AppliesTo(string action, IReadOnlyDictionary<string, string>? facts)
        => string.Equals(Action, action, StringComparison.Ordinal)
            && (Condition is null || Condition.Matches(facts));
}
