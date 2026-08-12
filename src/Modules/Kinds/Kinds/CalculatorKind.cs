using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using System.Globalization;

namespace DigitalBrain.Kinds;

// Total closed calculator: digits, one pending op, equals, clear.
// No host APIs — the kind tier's safety story starts here.
public sealed class CalculatorKind : ICellKind
{
    public const string KindName = "calculator";

    public static readonly CalculatorKind Instance = new();

    public string Name => KindName;

    public CellState Apply(CellState state, string key)
    {
        if (key.Length == 1 && char.IsDigit(key[0]))
        {
            return EnterDigit(state, key);
        }

        return key.ToUpperInvariant() switch
        {
            "." => EnterDot(state),
            "+" or "-" or "*" or "×" or "/" or "÷" => EnterOp(state, NormalizeOp(key)),
            "=" => Evaluate(state),
            "C" => CellState.Fresh(state.Kind, state.Instance),
            "CE" => state with { Display = "0", Value = 0, FreshEntry = true, Phase = "entry" },
            "BS" or "BACKSPACE" => Backspace(state),
            _ => throw new NeuronAuthorizationException(
                $"Calculator cell refuses key '{key}'. "
                + "Accepted: 0-9 . + - * / = C CE BS."),
        };
    }

    private static string NormalizeOp(string key)
        => key switch
        {
            "×" => "*",
            "÷" => "/",
            _ => key,
        };

    private static CellState EnterDigit(CellState state, string digit)
    {
        if (state.FreshEntry || state.Display is "0" or "Error")
        {
            return state with
            {
                Display = digit,
                Value = Parse(digit),
                FreshEntry = false,
                Phase = "entry",
            };
        }

        var display = state.Display + digit;
        return state with
        {
            Display = display,
            Value = Parse(display),
            Phase = "entry",
        };
    }

    private static CellState EnterDot(CellState state)
    {
        if (state.FreshEntry || state.Display is "Error")
        {
            return state with { Display = "0.", Value = 0, FreshEntry = false, Phase = "entry" };
        }

        if (state.Display.Contains('.', StringComparison.Ordinal))
        {
            return state;
        }

        var display = state.Display + ".";
        return state with { Display = display, Phase = "entry", FreshEntry = false };
    }

    private static CellState EnterOp(CellState state, string op)
    {
        if (state.Display is "Error")
        {
            return CellState.Fresh(state.Kind, state.Instance) with { PendingOp = op, Phase = "op" };
        }

        if (state.PendingOp is not null && !state.FreshEntry)
        {
            state = Evaluate(state);
            if (state.Display is "Error")
            {
                return state;
            }
        }

        var value = state.Value ?? Parse(state.Display);
        return state with
        {
            Accumulator = value,
            PendingOp = op,
            FreshEntry = true,
            Phase = "op",
            Value = value,
        };
    }

    private static CellState Evaluate(CellState state)
    {
        if (state.PendingOp is null)
        {
            return state with { Phase = "result", FreshEntry = true };
        }

        var right = state.Value ?? Parse(state.Display);
        var left = state.Accumulator;
        double result;
        try
        {
            result = state.PendingOp switch
            {
                "+" => left + right,
                "-" => left - right,
                "*" => left * right,
                "/" => right == 0
                    ? throw new DivideByZeroException()
                    : left / right,
                _ => throw new NeuronAuthorizationException(
                    $"Calculator has no operator '{state.PendingOp}'."),
            };
        }
        catch (DivideByZeroException)
        {
            return state with
            {
                Display = "Error",
                Value = null,
                PendingOp = null,
                FreshEntry = true,
                Phase = "error",
                Accumulator = 0,
            };
        }

        var display = Format(result);
        return state with
        {
            Display = display,
            Value = result,
            Accumulator = result,
            PendingOp = null,
            FreshEntry = true,
            Phase = "result",
        };
    }

    private static CellState Backspace(CellState state)
    {
        if (state.FreshEntry || state.Display is "Error" || state.Display.Length <= 1)
        {
            return state with { Display = "0", Value = 0, FreshEntry = true, Phase = "entry" };
        }

        var display = state.Display[..^1];
        if (display is "-" or ".")
        {
            display = "0";
        }

        return state with
        {
            Display = display,
            Value = Parse(display),
            Phase = "entry",
        };
    }

    private static double Parse(string text)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return 0;
    }

    private static string Format(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "Error";
        }

        return value.ToString("G15", CultureInfo.InvariantCulture);
    }
}
