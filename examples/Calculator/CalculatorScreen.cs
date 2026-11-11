/*
# Calculator

Role: Example
Shipped: true
Operations: calculator.open, calculator.clear, calculator.press

A reference DigitalBrain screen neuron demonstrating `Screen<TState>`, declarative `Render()`, and `ScreenBaseline` verification.
*/
using System.Globalization;
using DigitalBrain.Neurons;
using DigitalBrain.Rules;
using DigitalBrain.UI;

namespace DigitalBrain.Examples.Calculator;

[Signal("calculator.open")]
public sealed record CalculatorOpen : Signal;

[Signal("calculator.clear")]
public sealed record CalculatorClear : Signal;

[Signal("calculator.press")]
public sealed record CalculatorPress(string Key = "0") : Signal;

/// <summary>The immutable turn state of <see cref="CalculatorScreen"/>.</summary>
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

/// <summary>Reference calculator screen built on <see cref="Screen{TState}"/>.</summary>
[Neuron("app.calculator")]
public sealed class CalculatorScreen : Screen<CalculatorState>
{
    /// <summary>Renders the calculator view from <see cref="Screen{TState}.State"/>.</summary>
    protected override View Render(CalculatorState state)
        => new("Calculator", new Column([new Text(state.Display)]));

    /// <summary>Opens the calculator without resetting its current state.</summary>
    [On]
    public Task Open(CalculatorOpen signal) => Show(State);

    /// <summary>Resets the calculator state to <c>0</c>.</summary>
    [On]
    public Task Clear(CalculatorClear signal) => Show(new CalculatorState());

    /// <summary>Applies a digit or operator key to the calculator.</summary>
    [On]
    public async Task Press(CalculatorPress signal)
    {
        var (pending, divisor) = State.Pending(signal.Key);
        await Check(nameof(Press), new DivisorNotZero(pending, divisor)).ConfigureAwait(true);
        await Show(State.Apply(signal.Key)).ConfigureAwait(true);
    }
}
