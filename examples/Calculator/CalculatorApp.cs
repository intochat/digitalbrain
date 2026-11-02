/*
# Calculator

Role: Example
Shipped: true
Operations: open, clear, press

A clean reference DigitalBrain app demonstrating `App<TState>`, declarative `Render()`, and `[Scenario]` BDD verification.

## Scenario: Opening shows 0

Invoking "open" presents the display showing "0".

## Scenario: 7 + 5 = 12

Invoking "press" with "7", "+", "5", and "=" updates the display to "12".

## Scenario: Clear resets display to 0

Invoking "clear" resets the display back to "0".

## Scenario: Dividing by zero is rejected

Invoking "press" with "8", "/", "0", and "=" is rejected and the display still shows "0".
*/
using System.Globalization;
using DigitalBrain;
using DigitalBrain.Rules;
using DigitalBrain.UI;

namespace DigitalBrain.Examples.Calculator;

/// <summary>The immutable turn state of <see cref="CalculatorApp"/>.</summary>
public sealed record CalculatorState(
    string Display,
    double? Accumulator = null,
    string? PendingOperator = null,
    bool ResetNext = false)
{
    /// <summary>Initial state with `"0"` on the display.</summary>
    public CalculatorState() : this("0")
    {
    }

    /// <summary>Applies one keypad press and returns the next state.</summary>
    public CalculatorState Apply(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return this;
        }

        var token = key.Trim();
        if (token is "C" or "c")
        {
            return new CalculatorState();
        }

        if (token.Length == 1 && char.IsAsciiDigit(token[0]))
        {
            var nextDisplay = ResetNext || Display == "0" ? token : Display + token;
            return this with { Display = nextDisplay, ResetNext = false };
        }

        if (token is "+" or "-" or "*" or "/")
        {
            var current = double.TryParse(Display, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0d;
            var value = PendingOperator is not null && Accumulator is { } acc && !ResetNext
                ? Evaluate(acc, current, PendingOperator)
                : current;
            return new CalculatorState(
                Format(value),
                value,
                token,
                ResetNext: true);
        }

        if (token == "=" && PendingOperator is { } op && Accumulator is { } left)
        {
            var right = double.TryParse(Display, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0d;
            var result = Evaluate(left, right, op);
            return new CalculatorState(Format(result));
        }

        return this;
    }

    /// <summary>The pending operator and divisor that pressing <paramref name="key"/> would evaluate, or none when it evaluates nothing.</summary>
    internal (string? Operator, double Divisor) Pending(string? key)
    {
        var token = key?.Trim();
        var evaluates = token is "=" or "+" or "-" or "*" or "/"
            && PendingOperator is not null && Accumulator is not null && (token == "=" || !ResetNext);
        return evaluates && double.TryParse(Display, NumberStyles.Float, CultureInfo.InvariantCulture, out var divisor)
            ? (PendingOperator, divisor)
            : (null, 1d);
    }

    private static double Evaluate(double left, double right, string op) => op switch
    {
        "+" => left + right,
        "-" => left - right,
        "*" => left * right,
        "/" => left / right,
        _ => right,
    };

    private static string Format(double value) => value.ToString("G15", CultureInfo.InvariantCulture);
}

/// <summary>Reference calculator app built on <see cref="App{TState}"/>.</summary>
[App("calculator")]
public sealed class CalculatorApp : App<CalculatorState>
{
    /// <summary>Opens the calculator without resetting its current state.</summary>
    [On]
    public void Open() => Set(State);

    /// <summary>Resets the calculator state to <c>0</c>.</summary>
    [On]
    public void Clear() => Set(new CalculatorState());

    /// <summary>Applies a digit or operator key to the calculator.</summary>
    [On]
    public void Press(string key)
    {
        var (pending, divisor) = State.Pending(key);
        Check(nameof(Press), new DivisorNotZero(pending, divisor));
        Set(State.Apply(key));
    }

    /// <summary>Renders the calculator main view from <see cref="App{TState}.State"/>.</summary>
    public View Render() => new("Calculator", new Column([new Text(State.Display)]));

    /// <summary>BDD scenario: opening shows 0.</summary>
    [Scenario("Opening shows 0")]
    public static void OpeningShowsZero(CalculatorApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Open();
        if (app.State.Display != "0")
        {
            throw new InvalidOperationException($"Expected display '0', got '{app.State.Display}'.");
        }
    }

    /// <summary>BDD scenario: 7 + 5 = 12.</summary>
    [Scenario("7 + 5 = 12")]
    public static void SevenPlusFiveEqualsTwelve(CalculatorApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Press("7");
        app.Press("+");
        app.Press("5");
        app.Press("=");
        if (app.State.Display != "12")
        {
            throw new InvalidOperationException($"Expected display '12', got '{app.State.Display}'.");
        }
    }

    /// <summary>BDD scenario: Clear resets display to 0.</summary>
    [Scenario("Clear resets display to 0")]
    public static void ClearResetsDisplay(CalculatorApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Press("9");
        app.Clear();
        if (app.State.Display != "0")
        {
            throw new InvalidOperationException($"Expected display '0' after clear, got '{app.State.Display}'.");
        }
    }

    /// <summary>BDD scenario: Dividing by zero is rejected.</summary>
    [Scenario("Dividing by zero is rejected")]
    public static void DividingByZeroIsRejected(CalculatorApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Press("8");
        app.Press("/");
        app.Press("0");
        string[]? broken = null;
        try
        {
            app.Press("=");
        }
        catch (RuleViolationException error)
        {
            broken = error.Rejected.Rules;
            app.ClearError();
        }

        if (broken is not ["divisor.not-zero"] || app.State.Display != "0")
        {
            throw new InvalidOperationException($"Expected division by zero to be rejected with display '0', got '{app.State.Display}'.");
        }
    }
}
