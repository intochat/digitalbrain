using System.Globalization;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Core;

// One compiled grain interprets many durable kinds. Identity is the key:
// owner/{kind}@{instance}. Built-in kinds ship with the palette; later
// waves load kind records from a registry without a new GrainType.
[GrainType(ICell.GrainTypeName)]
internal sealed class CellNeuron : Neuron, ICell
{
    private const string StateName = "cell.state";
    private const char KindInstanceSeparator = '@';

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<CellState> _states;

    public CellNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<CellState>>();
    }

    public Task<CellSnapshot> Read()
        => Task.FromResult(SnapshotOf(LoadOrCreate()));

    public Task HandleAsync(CellApply synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        if (string.IsNullOrWhiteSpace(synapse.Key))
        {
            throw new NeuronAuthorizationException(
                $"Cell '{Id}' refuses an empty key. Send a digit, operator, '=', 'C', 'CE', or 'BS'.");
        }

        var identity = ParseIdentity();
        var kind = ResolveKind(identity.Kind);
        var state = LoadOrCreate();
        if (!string.Equals(state.Kind, identity.Kind, StringComparison.Ordinal))
        {
            state = CellState.Fresh(identity.Kind, identity.Instance);
        }

        state = kind.Apply(state, synapse.Key.Trim());
        Stage(state);

        return ReplyAsync(SnapshotOf(state), cancellationToken);
    }

    public Task HandleAsync(CellReset synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var identity = ParseIdentity();
        var state = CellState.Fresh(identity.Kind, identity.Instance);
        Stage(state);
        return ReplyAsync(SnapshotOf(state), cancellationToken);
    }

    private (string Kind, string Instance) ParseIdentity()
    {
        var name = Id.Name;
        var separator = name.IndexOf(KindInstanceSeparator, StringComparison.Ordinal);
        if (separator <= 0 || separator == name.Length - 1)
        {
            throw new NeuronAuthorizationException(
                $"Cell '{Id}' requires a key of the form kind@instance "
                + $"(got '{name}'). Example: calculator@main.");
        }

        return (name[..separator], name[(separator + 1)..]);
    }

    private static ICellKind ResolveKind(string kind)
    {
        if (string.Equals(kind, CalculatorKind.KindName, StringComparison.OrdinalIgnoreCase))
        {
            return CalculatorKind.Instance;
        }

        throw new NeuronAuthorizationException(
            $"Cell kind '{kind}' is not installed. Built-in kinds: {CalculatorKind.KindName}. "
            + "Install a kind record (later wave) or use calculator@{{name}}.");
    }

    private CellState LoadOrCreate()
    {
        if (_state.Value is { Length: > 0 } serialized)
        {
            return _states.Deserialize(serialized);
        }

        var identity = ParseIdentity();
        _ = ResolveKind(identity.Kind);
        return CellState.Fresh(identity.Kind, identity.Instance);
    }

    private void Stage(CellState data) => _state.Value = _states.SerializeToArray(data);

    private CellSnapshot SnapshotOf(CellState state)
        => new(state.Kind, state.Instance, state.Display, state.Value, state.Phase);

    private static void RequireCommand(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("A cell command requires a command id.");
        }
    }
}

[GenerateSerializer]
[Alias("db.cell-state")]
internal sealed record CellState(
    [property: Id(0)] string Kind,
    [property: Id(1)] string Instance,
    [property: Id(2)] string Display,
    [property: Id(3)] double? Value,
    [property: Id(4)] string Phase,
    [property: Id(5)] double Accumulator,
    [property: Id(6)] string? PendingOp,
    [property: Id(7)] bool FreshEntry)
{
    internal static CellState Fresh(string kind, string instance)
        => new(kind, instance, "0", 0, "idle", 0, null, true);
}

internal interface ICellKind
{
    string Name { get; }

    CellState Apply(CellState state, string key);
}

// Total closed calculator: digits, one pending op, equals, clear.
// No host APIs — the kind tier's safety story starts here.
internal sealed class CalculatorKind : ICellKind
{
    internal const string KindName = "calculator";

    internal static readonly CalculatorKind Instance = new();

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
