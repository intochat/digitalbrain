using System.Globalization;

namespace DigitalBrain.Limits;

/// <summary>Thrown when a proposed policy row violates the <see cref="PolicyGrammar"/>.</summary>
public sealed class PolicyGrammarException : Exception
{
    /// <summary>Creates a grammar exception with its message.</summary>
    /// <param name="message">The grammar violation description.</param>
    public PolicyGrammarException(string message) : base(message) { }

    /// <summary>Creates a grammar exception with its message and inner cause.</summary>
    /// <param name="message">The grammar violation description.</param>
    /// <param name="innerException">The underlying exception.</param>
    public PolicyGrammarException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Parses and renders <see cref="PolicyRule"/> rows against a declared <see cref="ActionDescriptor"/>.</summary>
public static class PolicyGrammar
{
    private static readonly TimeSpan MinimumRollingSpan = PolicyWindow.MinimumRollingSpan;

    /// <summary>Validates a policy row against <paramref name="declared"/> and renders its canonical sentence.</summary>
    /// <param name="id">The rule id.</param>
    /// <param name="action">The action name.</param>
    /// <param name="measure">The measure name (<c>Count</c> or <c>Sum</c>).</param>
    /// <param name="window">The window specification (<c>None</c>, <c>Day</c>, <c>Week</c>, <c>Month</c>, or <c>Rolling</c> with a span of at least one hour).</param>
    /// <param name="limit">The non-negative limit.</param>
    /// <param name="conditionFact">Optional fact key.</param>
    /// <param name="conditionOp">Optional operator name.</param>
    /// <param name="conditionValue">Optional comparison value.</param>
    /// <param name="declared">The action descriptor from the module manifest.</param>
    /// <param name="clock">The clock stamping <see cref="PolicyRule.AdoptedAt"/>.</param>
    /// <returns>The validated <see cref="PolicyRule"/> with its rendered <see cref="PolicyRule.Text"/>.</returns>
    /// <exception cref="PolicyGrammarException">Any field violates the policy grammar.</exception>
    public static PolicyRule Parse(
        string id,
        string action,
        string measure,
        string window,
        decimal limit,
        string? conditionFact,
        string? conditionOp,
        string? conditionValue,
        ActionDescriptor declared,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(clock);
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new PolicyGrammarException("grammar: rule id must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(action) || !HasActionShape(action) || !string.Equals(action, declared.Name, StringComparison.Ordinal))
        {
            throw new PolicyGrammarException($"grammar: action '{action}' does not match declared action '{declared.Name}'.");
        }

        var parsedMeasure = ParseMeasure(measure, declared);
        var parsedWindow = ParseWindow(window);
        if (limit < 0m)
        {
            throw new PolicyGrammarException($"grammar: limit '{limit}' must be non-negative.");
        }

        var condition = ParseCondition(conditionFact, conditionOp, conditionValue);
        var provisional = new PolicyRule(id, action, parsedMeasure, parsedWindow, limit, condition, string.Empty, clock.GetUtcNow());
        var text = Render(provisional, declared);
        return provisional with { Text = text };
    }

    /// <summary>Renders the canonical sentence shown to a person for <paramref name="rule"/>.</summary>
    /// <param name="rule">The policy rule.</param>
    /// <param name="declared">The action descriptor from the module manifest.</param>
    /// <returns>The rendered sentence.</returns>
    /// <exception cref="PolicyGrammarException">The rule is incompatible with <paramref name="declared"/> or violates the grammar.</exception>
    public static string Render(PolicyRule rule, ActionDescriptor declared)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(declared);
        if (!string.Equals(rule.Action, declared.Name, StringComparison.Ordinal) || !HasActionShape(rule.Action))
        {
            throw new PolicyGrammarException($"grammar: action '{rule.Action}' does not match declared action '{declared.Name}'.");
        }

        if (rule.Limit < 0m)
        {
            throw new PolicyGrammarException($"grammar: limit '{rule.Limit}' must be non-negative.");
        }

        var unit = rule.Measure switch
        {
            PolicyMeasure.Count => "count",
            PolicyMeasure.Sum when !string.IsNullOrWhiteSpace(declared.AmountUnit) => declared.AmountUnit!,
            PolicyMeasure.Sum => throw new PolicyGrammarException($"grammar: measure Sum requires an AmountUnit on action '{declared.Name}'."),
            _ => throw new PolicyGrammarException($"grammar: unknown measure '{rule.Measure}'."),
        };

        var windowText = RenderWindow(rule.Window);
        var limitText = rule.Limit.ToString("G29", CultureInfo.InvariantCulture);
        var sentence = $"Allow at most {limitText} {unit} of {declared.Description} per {windowText}";
        if (rule.Condition is { } c)
        {
            if (string.IsNullOrWhiteSpace(c.Fact) || c.Value is null || !Enum.IsDefined(c.Op))
            {
                throw new PolicyGrammarException("grammar: condition requires a non-empty fact, a valid operator and a value.");
            }

            sentence += $" when {c.Fact} {c.Op} {c.Value}";
        }

        return sentence;
    }

    private static bool HasActionShape(string action)
    {
        var dot = action.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && dot < action.Length - 1 && !action.StartsWith('.') && !action.EndsWith('.');
    }

    private static PolicyMeasure ParseMeasure(string measure, ActionDescriptor declared)
    {
        if (string.IsNullOrWhiteSpace(measure)
            || int.TryParse(measure, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            || !Enum.TryParse<PolicyMeasure>(measure, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new PolicyGrammarException($"grammar: unknown measure '{measure}'.");
        }

        if (parsed == PolicyMeasure.Sum && string.IsNullOrWhiteSpace(declared.AmountUnit))
        {
            throw new PolicyGrammarException($"grammar: measure Sum requires an AmountUnit on action '{declared.Name}'.");
        }

        return parsed;
    }

    private static PolicyWindow ParseWindow(string window)
    {
        if (string.IsNullOrWhiteSpace(window))
        {
            throw new PolicyGrammarException("grammar: window must not be empty.");
        }

        var trimmed = window.Trim();
        if (string.Equals(trimmed, nameof(PolicyWindowKind.None), StringComparison.OrdinalIgnoreCase))
        {
            return new PolicyWindow(PolicyWindowKind.None, null);
        }

        if (string.Equals(trimmed, nameof(PolicyWindowKind.Day), StringComparison.OrdinalIgnoreCase))
        {
            return new PolicyWindow(PolicyWindowKind.Day, null);
        }

        if (string.Equals(trimmed, nameof(PolicyWindowKind.Week), StringComparison.OrdinalIgnoreCase))
        {
            return new PolicyWindow(PolicyWindowKind.Week, null);
        }

        if (string.Equals(trimmed, nameof(PolicyWindowKind.Month), StringComparison.OrdinalIgnoreCase))
        {
            return new PolicyWindow(PolicyWindowKind.Month, null);
        }

        if (trimmed.StartsWith("Rolling", StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed["Rolling".Length..].Trim();
            if (rest.StartsWith(':') || rest.StartsWith('='))
            {
                rest = rest[1..].Trim();
            }
            else if (rest.StartsWith('(') && rest.EndsWith(')') && rest.Length >= 2)
            {
                rest = rest[1..^1].Trim();
            }

            if (!TryParseSpan(rest, out var span) || span < MinimumRollingSpan)
            {
                throw new PolicyGrammarException($"grammar: rolling window '{window}' must specify a TimeSpan of at least one hour.");
            }

            return new PolicyWindow(PolicyWindowKind.Rolling, span);
        }

        throw new PolicyGrammarException($"grammar: unknown window '{window}'.");
    }

    private static bool TryParseSpan(string text, out TimeSpan span)
    {
        span = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out span))
        {
            return true;
        }

        if (text.EndsWith('h') || text.EndsWith('H'))
        {
            if (double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) && hours > 0)
            {
                span = TimeSpan.FromHours(hours);
                return true;
            }
        }
        else if (text.EndsWith('m') || text.EndsWith('M'))
        {
            if (double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
            {
                span = TimeSpan.FromMinutes(minutes);
                return true;
            }
        }
        else if (text.EndsWith('d') || text.EndsWith('D'))
        {
            if (double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var days) && days > 0)
            {
                span = TimeSpan.FromDays(days);
                return true;
            }
        }

        return false;
    }

    private static PolicyCondition? ParseCondition(string? fact, string? op, string? value)
    {
        if (string.IsNullOrEmpty(fact) && string.IsNullOrEmpty(op) && string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(fact) || string.IsNullOrWhiteSpace(op) || value is null)
        {
            throw new PolicyGrammarException("grammar: condition requires fact, op and value.");
        }

        if (int.TryParse(op, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            || !Enum.TryParse<PolicyOp>(op, ignoreCase: true, out var parsedOp)
            || !Enum.IsDefined(parsedOp))
        {
            throw new PolicyGrammarException($"grammar: unknown condition operator '{op}'.");
        }

        return new PolicyCondition(fact, parsedOp, value);
    }

    private static string RenderWindow(PolicyWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.Kind switch
        {
            PolicyWindowKind.None => "call",
            PolicyWindowKind.Day => "day",
            PolicyWindowKind.Week => "week",
            PolicyWindowKind.Month => "month",
            PolicyWindowKind.Rolling when window.Span is { } span && span >= MinimumRollingSpan => $"rolling {span.ToString("c", CultureInfo.InvariantCulture)}",
            PolicyWindowKind.Rolling => throw new PolicyGrammarException("grammar: rolling window must specify a TimeSpan of at least one hour."),
            _ => throw new PolicyGrammarException($"grammar: unknown window kind '{window.Kind}'."),
        };
    }
}
