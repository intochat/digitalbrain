using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Brains;

// The brain a call is in. Its id is its kernel's grain key and the prefix of every neuron key inside it.
public sealed record BrainScope(string Id, string Owner)
{
    public static string CurrentId()
    {
        if (!CallerAdmission.TryGetCaller(out var caller))
        { throw new InvalidOperationException("No caller context is stamped on this call."); }
        return Create(caller.AccountId, caller.BrainId).Id;
    }

    public static BrainScope Create(string owner, string name)
    {
        if (!IsValidId(name))
        { throw new ArgumentException("A brain ID must contain 1–200 characters without path separators or ':'.", nameof(name)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        return new(name, owner);
    }

    // Brain, thread and run ids travel in routes and grain keys: 1-200 characters, no control
    // characters, no path separators and no ':'. A leading '@' is reserved for keys outside every
    // brain ("@platform/...").
    public static bool IsValidId([NotNullWhen(true)] string? value) => NeuronAddress.IsBrainId(value);
}
