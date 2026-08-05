namespace DigitalBrain;

// Neurons read time through a keyed TimeProvider so a controllable clock can be given to
// the brain without being given to Orleans: the runtime resolves the unkeyed provider for
// activation collection and timer scheduling, and a far-future test epoch there throws.
public static class NeuronTime
{
    public const string ServiceKey = "digitalbrain.clock";
}
