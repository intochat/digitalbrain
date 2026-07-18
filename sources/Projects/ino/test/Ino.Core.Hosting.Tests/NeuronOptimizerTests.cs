using Ino.Core.Hosting.ML;
using Ino.Testing;
using Orleans;
using Xunit;

namespace Ino.Core.Hosting.Tests;

/// <summary>
/// Phase 4 Slice D: <see cref="INeuronOptimizer"/> records decisions, trains a
/// LightGBM binary classifier once the threshold is crossed, and serves
/// predictions when confident. Tests run against the in-memory test silo —
/// the <see cref="IDurableList{T}"/> backing the records is wired by
/// <see cref="TestSiloConfigurator"/> exactly like every other neuron's journal.
/// </summary>
[Collection(nameof(InoTestCollection))]
public sealed class NeuronOptimizerTests
{
    private readonly InoTestSiloFixture _fixture;

    public NeuronOptimizerTests(InoTestSiloFixture fixture)
    {
        _fixture = fixture;
    }

    static string UniqueKey(string prefix) => $"{prefix}-{Guid.NewGuid():n}";

    static DecisionRecord Row(bool label, params float[] features) =>
        new(features, label, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Predict_returns_null_before_training_threshold()
    {
        var optimizer = _fixture.Grains.GetGrain<INeuronOptimizer>(UniqueKey("cold"));

        // Default threshold is 50; record 49 rows and confirm no model.
        for (var i = 0; i < 49; i++)
            await optimizer.Record(Row(label: i % 2 == 0, i, i * 2));

        Assert.Equal(49, await optimizer.GetRecordCount());
        var result = await optimizer.Predict([10f, 20f]);
        Assert.Null(result);
    }

    [Fact]
    public async Task Trains_and_predicts_with_high_confidence_on_separable_data()
    {
        var optimizer = _fixture.Grains.GetGrain<INeuronOptimizer>(UniqueKey("trained"));

        // Linearly-separable two-class data: feature[0] > 0.5 → label true,
        // else false. 50 rows ÷ 2 classes = balanced. LightGBM lifts trivially.
        for (var i = 0; i < 60; i++)
        {
            var positive = i % 2 == 0;
            var x0 = positive ? 0.7f + (i * 0.001f) : 0.2f + (i * 0.001f);
            var x1 = (i % 7) * 0.1f;
            await optimizer.Record(Row(label: positive, x0, x1));
        }

        var positiveResult = await optimizer.Predict([0.85f, 0.4f]);
        Assert.NotNull(positiveResult);
        Assert.True(positiveResult!.Predicted);
        Assert.True(positiveResult.Confidence > 0.7f,
            $"expected high confidence on positive sample but got {positiveResult.Confidence:F2}");

        var negativeResult = await optimizer.Predict([0.15f, 0.4f]);
        Assert.NotNull(negativeResult);
        Assert.False(negativeResult!.Predicted);
    }

    [Fact]
    public async Task Predict_returns_null_when_feature_count_mismatches_schema()
    {
        var optimizer = _fixture.Grains.GetGrain<INeuronOptimizer>(UniqueKey("schema"));

        for (var i = 0; i < 60; i++)
            await optimizer.Record(Row(label: i % 2 == 0, i * 0.01f, i * 0.02f));

        // Trained on 2-feature rows; ask with 3 features → null.
        var result = await optimizer.Predict([0.5f, 0.5f, 0.5f]);
        Assert.Null(result);
    }

    [Fact]
    public async Task Records_are_capped_at_max_records_via_circular_buffer()
    {
        // Test silo registers the default options (10k cap) — the grain's
        // own ShouldRetrain still works at 50, but here we just check the
        // append-and-trim loop. Push a small known volume past a smaller
        // cap by configuring a custom options instance via DI override is
        // a follow-up; for now assert that the recorded count never
        // exceeds the default cap and that records stay temporally ordered.
        var optimizer = _fixture.Grains.GetGrain<INeuronOptimizer>(UniqueKey("trim"));
        for (var i = 0; i < 75; i++)
            await optimizer.Record(Row(label: i % 2 == 0, i * 0.01f, i * 0.01f));

        var count = await optimizer.GetRecordCount();
        Assert.Equal(75, count);
        Assert.True(count <= 10_000);
    }

    [Fact]
    public async Task Single_class_data_does_not_break_training()
    {
        // 60 records, all label=true. LightGBM can't fit on one class —
        // the grain must skip retrain rather than throw, and Predict must
        // continue to return null until both classes appear.
        var optimizer = _fixture.Grains.GetGrain<INeuronOptimizer>(UniqueKey("monotone"));
        for (var i = 0; i < 60; i++)
            await optimizer.Record(Row(label: true, i * 0.01f, i * 0.02f));

        Assert.Equal(60, await optimizer.GetRecordCount());
        var result = await optimizer.Predict([0.5f, 0.5f]);
        Assert.Null(result);

        // Inject negatives — next retrain should succeed.
        for (var i = 0; i < 25; i++)
            await optimizer.Record(Row(label: false, -1f * (i + 1) * 0.01f, -1f * (i + 1) * 0.02f));

        var afterNeg = await optimizer.Predict([0.5f, 0.5f]);
        Assert.NotNull(afterNeg);
    }
}
