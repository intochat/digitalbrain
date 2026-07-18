using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML;
using Microsoft.ML.Data;
using Orleans;
using Orleans.Journaling;

namespace Ino.Core.Hosting.ML;

/// <summary>
/// Default <see cref="INeuronOptimizer"/> implementation. Records persist on
/// an <see cref="IDurableList{T}"/>; LightGBM is trained in-memory from the
/// journal on activation and on each retrain trigger. The model itself is
/// not journaled — re-training from up to 10k rows takes &lt;100ms for a
/// modest feature schema, which is faster than serialising a LightGBM model
/// to/from binary blobs across silo restarts.
/// </summary>
public class NeuronOptimizerGrain(
    [FromKeyedServices("optimizer-records")] IDurableList<DecisionRecord> records,
    NeuronOptimizerOptions? options = null,
    ILogger<NeuronOptimizerGrain>? log = null)
    : DurableGrain, INeuronOptimizer
{
    private readonly NeuronOptimizerOptions _opts = options ?? new();
    private readonly ILogger _log = (ILogger?)log ?? NullLogger.Instance;
    private readonly MLContext _ml = new(seed: 1);

    private ITransformer? _model;
    private PredictionEngine<MlRow, MlPrediction>? _engine;
    private int _featureCount = -1;
    private int _recordsSinceLastTrain;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        TryTrain(initial: true);
    }

    public async Task Record(DecisionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Features.Length == 0) return;

        if (_featureCount > 0 && record.Features.Length != _featureCount)
        {
            // Schema drift drops the row rather than tearing down the model
            // — Phase 4 Slice D doesn't ship a schema-evolution story; that
            // lands when FeatureArchitect goes in (post-v0.1).
            _log.LogWarning(
                "NeuronOptimizer {Key}: dropped record with {Got} features (expected {Expected})",
                this.GetPrimaryKeyString(), record.Features.Length, _featureCount);
            return;
        }

        records.Add(record);

        // Circular-buffer trim: oldest records evicted when over MaxRecords.
        // IDurableList exposes RemoveAt for in-place mutation.
        while (records.Count > _opts.MaxRecords)
            records.RemoveAt(0);

        await WriteStateAsync();

        _recordsSinceLastTrain++;
        if (ShouldRetrain()) TryTrain(initial: false);
    }

    public Task<OptimizationResult?> Predict(float[] features)
    {
        ArgumentNullException.ThrowIfNull(features);
        if (_engine is null || _featureCount <= 0)
            return Task.FromResult<OptimizationResult?>(null);
        if (features.Length != _featureCount)
            return Task.FromResult<OptimizationResult?>(null);

        var prediction = _engine.Predict(new MlRow { Features = features });
        var confidence = prediction.Predicted ? prediction.Probability : 1f - prediction.Probability;
        return Task.FromResult<OptimizationResult?>(
            new OptimizationResult(prediction.Predicted, confidence));
    }

    public Task<int> GetRecordCount() => Task.FromResult(records.Count);

    bool ShouldRetrain()
    {
        if (records.Count < _opts.TrainThreshold) return false;
        if (_model is null) return true;
        return _recordsSinceLastTrain >= _opts.RetrainInterval;
    }

    void TryTrain(bool initial)
    {
        if (records.Count < _opts.TrainThreshold)
        {
            if (!initial)
                _log.LogDebug(
                    "NeuronOptimizer {Key}: train skipped, {Count} < {Threshold}",
                    this.GetPrimaryKeyString(), records.Count, _opts.TrainThreshold);
            return;
        }

        // Materialise the training set inside this method so trim/append
        // mutations between calls don't create torn views. IDurableList
        // enumerates in insertion order; that's the temporal order we want
        // for time-aware feature inputs.
        var rows = new List<MlRow>(records.Count);
        var inferredFeatureCount = 0;
        var positiveCount = 0;
        foreach (var rec in records)
        {
            if (rec.Features.Length == 0) continue;
            if (inferredFeatureCount == 0) inferredFeatureCount = rec.Features.Length;
            else if (rec.Features.Length != inferredFeatureCount) continue;

            rows.Add(new MlRow { Features = rec.Features, Label = rec.Label });
            if (rec.Label) positiveCount++;
        }

        if (rows.Count < _opts.TrainThreshold || inferredFeatureCount == 0)
            return;

        // LightGBM needs both classes present; degenerate one-class data
        // (e.g. every decision succeeded so far) breaks training. Skip
        // until divergent data shows up.
        if (positiveCount == 0 || positiveCount == rows.Count)
        {
            _log.LogDebug(
                "NeuronOptimizer {Key}: train skipped, single-class data ({Pos}/{Total})",
                this.GetPrimaryKeyString(), positiveCount, rows.Count);
            return;
        }

        try
        {
            // LightGbm requires a fixed-size vector column. Build a schema
            // definition that pins Features to the actual width — without
            // this the trainer rejects the column as "Vec<Single, -1>".
            var schema = SchemaDefinition.Create(typeof(MlRow));
            schema[nameof(MlRow.Features)].ColumnType = new VectorDataViewType(
                NumberDataViewType.Single, inferredFeatureCount);

            var view = _ml.Data.LoadFromEnumerable(rows, schema);
            var pipeline = _ml.BinaryClassification.Trainers.LightGbm(
                labelColumnName: nameof(MlRow.Label),
                featureColumnName: nameof(MlRow.Features));
            _model = pipeline.Fit(view);
            _engine = _ml.Model.CreatePredictionEngine<MlRow, MlPrediction>(
                _model, inputSchemaDefinition: schema);
            _featureCount = inferredFeatureCount;
            _recordsSinceLastTrain = 0;
            _log.LogInformation(
                "NeuronOptimizer {Key}: retrained on {Count} rows ({Positive} positive, {Features} features)",
                this.GetPrimaryKeyString(), rows.Count, positiveCount, inferredFeatureCount);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "NeuronOptimizer {Key}: retrain failed; previous model retained",
                this.GetPrimaryKeyString());
        }
    }

    /// <summary>
    /// ML.NET row schema. <see cref="VectorTypeAttribute"/> is sized
    /// dynamically by <see cref="DataViewSchema"/> at <c>LoadFromEnumerable</c>
    /// time, so the same class works for any feature-count schema.
    /// </summary>
    sealed class MlRow
    {
        [VectorType]
        public float[] Features { get; set; } = Array.Empty<float>();
        public bool Label { get; set; }
    }

    sealed class MlPrediction
    {
        [ColumnName("PredictedLabel")] public bool Predicted { get; set; }
        public float Probability { get; set; }
        public float Score { get; set; }
    }
}
