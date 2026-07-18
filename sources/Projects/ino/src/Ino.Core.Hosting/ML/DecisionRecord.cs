namespace Ino.Core.Hosting.ML;

/// <summary>
/// One row of training data captured by a neuron's decision loop. The
/// <see cref="Features"/> vector is the schema-aligned float encoding of
/// inputs at decision time; <see cref="Label"/> is the binary outcome
/// (1 = success / chosen path worked, 0 = failed / fell through).
///
/// Stored on the optimizer grain's <see cref="Orleans.Journaling.IDurableList{T}"/>
/// so records survive silo restarts. The grain re-trains LightGBM from the
/// journal on activation; the model itself is in-memory only (cheap to
/// rebuild from up to 10k rows in milliseconds).
/// </summary>
[GenerateSerializer]
public sealed record DecisionRecord(
    [property: Id(0)] float[] Features,
    [property: Id(1)] bool Label,
    [property: Id(2)] DateTimeOffset Timestamp);
