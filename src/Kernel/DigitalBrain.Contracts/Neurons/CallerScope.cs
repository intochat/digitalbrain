using DigitalBrain.Abstractions.Identity;
using Orleans.Runtime;

namespace DigitalBrain.Abstractions.Neurons;

// Pushes the caller onto RequestContext for a call and restores the previous value, so an edge and the outgoing filter stamp attribution the same way.
public readonly struct CallerScope : IDisposable
{
    private readonly object? _previous;

    private CallerScope(NeuronId caller)
    {
        _previous = RequestContext.Get(NeuronRequestKeys.Caller);
        RequestContext.Set(NeuronRequestKeys.Caller, caller.ToString());
    }

    public static CallerScope For(NeuronId caller) => new(caller);

    public void Dispose()
    {
        if (_previous is null)
        {
            RequestContext.Remove(NeuronRequestKeys.Caller);
        }
        else
        {
            RequestContext.Set(NeuronRequestKeys.Caller, _previous);
        }
    }
}
