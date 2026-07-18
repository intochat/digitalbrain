namespace Ino.Core.Hosting.ML;

/// <summary>
/// Result of a <see cref="INeuronOptimizer.Predict"/> call. Null is returned
/// (rather than this type) when no model has been trained yet — the consumer
/// then falls through to the LLM path. When a result is returned,
/// <see cref="Confidence"/> is the model's posterior probability of the
/// predicted label and the consumer typically gates on
/// <c>Confidence &gt;= 0.90</c> before short-circuiting the LLM.
/// </summary>
[GenerateSerializer]
public sealed record OptimizationResult(
    [property: Id(0)] bool Predicted,
    [property: Id(1)] float Confidence);
