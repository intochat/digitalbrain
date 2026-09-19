using DigitalBrain.Abstractions.Identity;
using Orleans.Runtime;

namespace DigitalBrain.Abstractions.Neurons;

public readonly struct CallerScope : IDisposable
{
    private readonly object? _previous;

    private CallerScope(string caller)
    {
        _previous = RequestContext.Get(NeuronRequestKeys.Caller);
        RequestContext.Set(NeuronRequestKeys.Caller, caller);
    }

    public static CallerScope For(INeuron caller) => new(NeuronId.FromGrainId(caller.GetGrainId()).ToString());

    public static CallerScope For(NeuronId caller) => new(caller.ToString());

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
