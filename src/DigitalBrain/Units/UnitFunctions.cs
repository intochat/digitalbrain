using System.Reflection;
using System.Runtime.ExceptionServices;
using DigitalBrain.Invocation;
using DigitalBrain.Spi;

namespace DigitalBrain.Units;

internal readonly record struct UnitCall(Signal? Signal, string? TraceId, string? CausationId)
{
    public static readonly UnitCall None = new(null, null, null);
    public SignalEnvelope? Envelope => Signal is null ? null : BuildEnvelope(Signal, TraceId, CausationId);

    private static SignalEnvelope BuildEnvelope(Signal signal, string? traceId, string? causationId)
    {
        if (signal is IUntypedSignal untyped) { return new SignalEnvelope(untyped.Alias, untyped.Payload, traceId, causationId); }
        var type = signal.GetType();
        return new SignalEnvelope(SignalAliases.Of(signal), System.Text.Json.JsonSerializer.SerializeToElement(signal, type, RuntimeProtocol.OptionsFor(type)), traceId, causationId);
    }
}

internal sealed class BoundFunction
{
    private enum Take { State, Signal, Envelope, Cancellation, Slot, Answer }

    private readonly Take[] _takes;
    private readonly SlotDescriptor?[] _slots;
    private readonly Type? _awaited;
    private readonly TimeSpan _budget;
    private readonly int _answerIndex = -1;
    private readonly int _stateIndex = -1;

    public BoundFunction(SynapseDescriptor shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        Shape = shape;
        var parameters = shape.Function.GetParameters();
        _slots = new SlotDescriptor?[parameters.Length];
        foreach (var slot in shape.Slots) { _slots[slot.ParameterIndex] = slot; }
        _takes = new Take[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            if (_slots[i] is not null) { _takes[i] = Take.Slot; }
            else if (p.ParameterType == typeof(Answer)) { _takes[i] = Take.Answer; _answerIndex = i; }
            else if (p.ParameterType == shape.State) { _takes[i] = Take.State; _stateIndex = i; }
            else if (p.ParameterType == typeof(CancellationToken)) { _takes[i] = Take.Cancellation; }
            else if (p.ParameterType == typeof(SignalEnvelope)) { _takes[i] = Take.Envelope; }
            else if (typeof(Signal).IsAssignableFrom(p.ParameterType)) { _takes[i] = Take.Signal; }
            else { _takes[i] = Take.State; _stateIndex = i; }
        }

        var returned = shape.Function.ReturnType;
        _awaited = returned.IsGenericType && typeof(Task).IsAssignableFrom(returned) ? returned.GetGenericArguments()[0] : null;
        _budget = shape.Slots.Any(s => !s.Deferred) ? UnitLimits.SlotReactionTimeout : UnitLimits.ReactionTimeout;
    }

    public SynapseDescriptor Shape { get; }
    public string Name => Shape.Name;

    public async Task<object?> Invoke(UnitCall call, object state, ISlotResolver slots, NeuronId unit, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(_budget);
        var token = budget.Token;

        var arguments = new object?[_takes.Length];
        for (var i = 0; i < _takes.Length; i++)
        {
            arguments[i] = _takes[i] switch
            {
                Take.State => state,
                Take.Signal => call.Signal,
                Take.Envelope => call.Envelope,
                Take.Cancellation => token,
                Take.Slot => _slots[i]!.Deferred
                    ? Activator.CreateInstance(typeof(Asks<>).MakeGenericType(_slots[i]!.Contract), _slots[i]!.Name)
                    : await slots.Resolve(unit, _slots[i]!, token).ConfigureAwait(true),
                _ => null
            };
        }

        if (Shape.PerAnswer && call.Signal is Answered answered)
        {
            return InvokePerAnswer(answered, state, arguments, token);
        }

        object? returned;
        try
        {
            returned = Shape.Function.Invoke(null, BindingFlags.DoNotWrapExceptions, null, arguments, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        if (returned is not Task task) { return returned; }
        await task.WaitAsync(token).ConfigureAwait(true);
        return await TaskBoxing.ResultAsync(task, _awaited).ConfigureAwait(true);
    }

    private object? InvokePerAnswer(Answered answered, object state, object?[] arguments, CancellationToken token)
    {
        var current = state;
        var signals = new List<Signal>();
        var asks = new List<Ask>();
        var touched = false;

        foreach (var answer in answered.Answers)
        {
            token.ThrowIfCancellationRequested();
            arguments[_answerIndex] = answer;
            arguments[_stateIndex] = current;
            object? step;
            try
            {
                step = Shape.Function.Invoke(null, BindingFlags.DoNotWrapExceptions, null, arguments, null);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }

            if (step is null) continue;
            var stepType = step.GetType();
            if (stepType.IsGenericType && stepType.GetGenericTypeDefinition() == typeof(Reaction<>))
            {
                var next = stepType.GetProperty("Next")!.GetValue(step)!;
                var sigs = (IReadOnlyList<Signal>)stepType.GetProperty("Signals")!.GetValue(step)!;
                var ak = (IReadOnlyList<Ask>)stepType.GetProperty("Asks")!.GetValue(step)!;
                touched |= !ReferenceEquals(next, current) || sigs.Count > 0 || ak.Count > 0;
                current = next;
                signals.AddRange(sigs);
                asks.AddRange(ak);
            }
            else
            {
                touched |= !ReferenceEquals(step, current);
                current = step;
            }
        }

        return touched ? CreateReaction(current, signals, asks) : state;
    }

    private static object CreateReaction(object next, IReadOnlyList<Signal> signals, IReadOnlyList<Ask> asks)
    {
        var reactionType = typeof(Reaction<>).MakeGenericType(next.GetType());
        var reaction = Activator.CreateInstance(reactionType, next, signals)!;
        reactionType.GetProperty("Asks")!.SetValue(reaction, asks);
        return reaction;
    }
}

internal sealed class UnitFunctions<TState>(string alias, IReadOnlyList<BoundFunction> bound) where TState : class
{
    private readonly BoundFunction[] _reactions = [.. bound.Where(f => f.Shape.Kind == SynapseKind.Reaction)];

    public string Alias => alias;
    public IReadOnlyList<BoundFunction> Views { get; } = [.. bound.Where(f => f.Shape.Kind == SynapseKind.View)];
    public IReadOnlyList<SynapseDescriptor> Descriptors { get; } = [.. bound.Select(b => b.Shape)];

    public static UnitFunctions<TState>? Bind(SynapseCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var name = catalog.States.FirstOrDefault(s => s.Value == typeof(TState)).Key;
        return name is null ? null : new(name, [.. catalog.For(name).Select(s => new BoundFunction(s))]);
    }

    public IEnumerable<BoundFunction> ReactionsTo(Signal signal)
    {
        var arriving = SignalAliases.Of(signal);
        return _reactions.Where(f => f.Shape.SignalAlias is { } named
            ? string.Equals(named, arriving, StringComparison.Ordinal)
            : signal is not IUntypedSignal && (f.Shape.Signal!.IsInstanceOfType(signal)
                || (f.Shape.Signal.GetCustomAttribute<SignalAttribute>(false)?.Alias is { } a && string.Equals(a, arriving, StringComparison.Ordinal))));
    }
}
