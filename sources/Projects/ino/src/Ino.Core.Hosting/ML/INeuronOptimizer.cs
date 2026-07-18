using Orleans;

namespace Ino.Core.Hosting.ML;

/// <summary>
/// Per-neuron+user LightGBM classifier. Records every decision the host
/// neuron makes (<see cref="Record"/>), trains automatically once the
/// record count crosses the threshold, and serves predictions
/// (<see cref="Predict"/>) when enough confidence is available. Returns
/// <c>null</c> from <see cref="Predict"/> before the first model is trained
/// so the host falls back to the LLM path.
///
/// Primary key shape is the host's choice; the convention is
/// <c>"{neuron-kind}-{userId}"</c> (e.g. <c>"cortex-{userId}"</c>) so a
/// per-user model evolves alongside the user's history without crossing
/// tenant boundaries.
/// </summary>
public interface INeuronOptimizer : IGrainWithStringKey
{
    /// <summary>
    /// Append a decision row. Triggers a retrain when the record count
    /// crosses <see cref="NeuronOptimizerOptions.TrainThreshold"/> or when
    /// the count since the last train exceeds
    /// <see cref="NeuronOptimizerOptions.RetrainInterval"/>. Both run synchronously
    /// inside the grain's single-threaded turn — typical retrain on 10k
    /// rows completes in &lt;100ms for the 5-feature schema, and the records
    /// are bounded by <see cref="NeuronOptimizerOptions.MaxRecords"/>
    /// (oldest dropped).
    /// </summary>
    Task Record(DecisionRecord record);

    /// <summary>
    /// Predict the label for an unseen feature vector. Returns null when
    /// no model has been trained yet (record count below threshold), the
    /// schema doesn't match, or LightGBM training failed on the last
    /// retrain.
    /// </summary>
    Task<OptimizationResult?> Predict(float[] features);

    /// <summary>
    /// How many rows are currently retained in the journal — useful for
    /// tests and the inspector ML panel. Includes both used and to-be-
    /// trimmed rows when <see cref="NeuronOptimizerOptions.MaxRecords"/>
    /// is exceeded.
    /// </summary>
    Task<int> GetRecordCount();
}
