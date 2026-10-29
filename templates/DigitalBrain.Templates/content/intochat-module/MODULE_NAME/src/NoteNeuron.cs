using DigitalBrain;
using MODULE_NAMESPACE.Contracts;

namespace MODULE_NAMESPACE;

/// <summary>Published when a note is recorded.</summary>
[Signal("MODULE_ID.recorded")]
public sealed record Recorded(string Text) : Signal;

[Neuron("note")]
internal sealed class NoteNeuron : Neuron, IMODULE_CLASS
{
    public Task Record(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return Publish(new Recorded(text));
    }
}
