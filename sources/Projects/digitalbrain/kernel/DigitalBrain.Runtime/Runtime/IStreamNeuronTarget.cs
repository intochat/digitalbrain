namespace DigitalBrain.Runtime.Runtime;

// Sibling of ICallNeuronTarget (E-SDK #45) for neurons whose natural shape is a
// stream of chunks rather than a single reply — `ask $stream to "transcribe ..."`,
// token-streaming LLMs, partial-result feeds. Orleans 10.x supports
// IAsyncEnumerable<T> grain returns natively; implementors mark their
// CancellationToken parameter with [EnumeratorCancellation] so early disposal
// on the caller side stops the grain promptly. The attribute is only valid
// on the async-iterator method body, not on the interface declaration here.
//
// IGrainWithStringKey for the same reason as ICallNeuronTarget: when InoLang
// supplies `["key"]` it is a string, and ProductionNeuronHost substitutes the
// TargetFqn as a singleton-per-type default when no key is given (Orleans
// rejects empty primary keys).
public interface IStreamNeuronTarget : IGrainWithStringKey
{
    IAsyncEnumerable<string> StreamAsync(string prompt, CancellationToken ct);
}
