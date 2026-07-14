using DigitalBrain.Features.Sdk;
namespace DigitalBrain.Features.Testing;

public sealed class GeneratedFeatureScenario(FeatureScenarioContext scenario)
{
    private CountingFeature? _feature;
    private FeatureInput? _input;
    public FeatureScenarioResult? FirstResult { get; private set; }
    public FeatureScenarioResult? SecondResult { get; private set; }
    public int HandlerExecutionCount => _feature?.ExecutionCount ?? 0;
    public void Configure(IFeature feature, FeatureInput input)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(input);
        _feature = new CountingFeature(feature);
        _input = input;
        FirstResult = null;
        SecondResult = null;
    }
    public async Task ExecuteTwiceAsync(CancellationToken cancellationToken = default)
    {
        var feature = _feature ?? throw new InvalidOperationException("Configure the generated Feature scenario before executing it.");
        var input = _input ?? throw new InvalidOperationException("Configure the generated Feature input before executing it.");
        FirstResult = await scenario.ExecuteAsync(feature, input, cancellationToken);
        SecondResult = await scenario.ExecuteAsync(feature, input, cancellationToken);
    }
    private sealed class CountingFeature(IFeature inner) : IFeature
    {
        private int _executionCount;
        public int ExecutionCount => Volatile.Read(ref _executionCount);
        public Task HandleAsync(FeatureInput input, IFeatureContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executionCount);
            return inner.HandleAsync(input, context, cancellationToken);
        }
    }
}
