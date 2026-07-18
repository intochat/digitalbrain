namespace DigitalBrain.Runtime.Runtime;

// The production binding of an InoLang `using $port = neuron(Target)` /
// `using ~port = neuron(Target["key"])` declaration. Every neuron grain
// implementation declares `[GrainType("TargetFqn")]` matching the .ino's
// TargetFqn 1:1; the production neuron host then dispatches via
// IGrainFactory.GetGrain<ICallNeuronTarget>(grainId) where the GrainId is
// built from GrainType.Create(TargetFqn) + the neuron key. GrainId addressing
// is the right knob because Orleans's `grainClassNamePrefix` parameter
// matches the C# class FullName, not the GrainType id — a dotted .ino FQN
// can never prefix-match a C# class name. See ProductionNeuronHost for the
// full rationale.
//
// IGrainWithStringKey because the Key from `["key"]` is a string when
// present. The host substitutes the TargetFqn as a singleton-per-type
// default when no key is given, since Orleans rejects empty primary keys.
public interface ICallNeuronTarget : IGrainWithStringKey
{
    Task<string> AskAsync(string prompt);
}
