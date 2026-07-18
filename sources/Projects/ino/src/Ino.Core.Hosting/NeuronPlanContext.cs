using Ino.Core;

namespace Ino.Core.Hosting;

/// <summary>
/// Wire-safe input passed to <see cref="INeuronPlan.ExecuteAsync"/>. Carries
/// the natural-language prompt, the originating Cortex <see cref="NeuronContext"/>
/// (for correlation/causation), and the matched neuron id.
///
/// <see cref="NeuronContext"/> serializes via <see cref="NeuronContextSurrogate"/>
/// — its <c>FirePort</c> and <c>Logger</c> fields are non-marshalable and arrive
/// as no-op stubs on the receiving silo. Plan implementations rebuild a usable
/// context from their own DI before calling <see cref="ITraversalEngine"/>.
/// </summary>
[GenerateSerializer]
public sealed record NeuronPlanContext(
    [property: Id(0)] string Prompt,
    [property: Id(1)] NeuronContext Caller,
    [property: Id(2)] NeuronId NeuronId);
