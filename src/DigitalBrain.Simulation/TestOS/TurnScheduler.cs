using DigitalBrain.Contracts;
using System.Reflection;
using DigitalBrain.Invocation;
using DigitalBrain.Kernel;

namespace DigitalBrain.Testing;

// One neuron's turn: a call takes it unless it interleaves. Work always leaves the caller's synchronous stack first,
// so a queued call never runs inside the caller's turn.
internal sealed class TurnScheduler : IDisposable
{
    private readonly SemaphoreSlim _turn = new(1, 1);

    public async Task<T> Run<T>(bool interleave, Func<Task<T>> body)
    {
        if (interleave)
        {
            await Task.Yield();
            return await body().ConfigureAwait(false);
        }

        var wait = _turn.WaitAsync();
        await Task.Yield();
        await wait.ConfigureAwait(false);
        try
        {
            return await body().ConfigureAwait(false);
        }
        finally
        {
            _turn.Release();
        }
    }

    public void Dispose() => _turn.Dispose();
}

internal static class CallMarkers
{
    public static (bool Interleaves, bool OneWay) Of(Type neuronClass, MethodInfo contractMethod, RuntimeInvocation call)
    {
        var interleave = CallPolicy.MayInterleave(neuronClass, contractMethod, call);
        var oneWay = contractMethod.IsDefined(typeof(OneWayCallAttribute), false);
        return (interleave, oneWay);
    }

    public static Func<RuntimeInvocation, bool>? Predicate(Type neuronClass)
        => CallPolicy.PredicateOf(neuronClass);
}
