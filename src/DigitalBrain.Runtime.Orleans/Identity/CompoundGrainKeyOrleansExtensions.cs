using Orleans.Runtime;

namespace DigitalBrain.Identity;

/// <summary>
/// Orleans-specific IdSpan conversions for <see cref="CompoundGrainKey"/> and <see cref="NeuronId"/>.
/// </summary>
public static class CompoundGrainKeyOrleansExtensions
{
    public static IdSpan ToIdSpan(this CompoundGrainKey key) => IdSpan.Create(key.ToString());

    public static IdSpan ToIdSpan(this NeuronId neuronId) => neuronId.ToCompoundGrainKey().ToIdSpan();

    public static CompoundGrainKey Parse(IdSpan span) => CompoundGrainKey.Parse(span.ToString());

    public static bool TryParse(IdSpan span, out CompoundGrainKey key) => CompoundGrainKey.TryParse(span.ToString(), out key);

    public static CompoundGrainKey ToCompoundGrainKey(this IdSpan span) => CompoundGrainKey.Parse(span.ToString());
}
