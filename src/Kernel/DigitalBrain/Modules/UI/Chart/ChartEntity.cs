using DigitalBrain.Core.Neurons;

namespace DigitalBrain.Modules.UI.Chart;

public class ChartEntity([PersistentState("chartState", "memory")] IPersistentState<ChartState> state)
    : Entity<ChartState>(state), IChartEntity
{
    public async Task AddPointAsync(ChartPoint point, CancellationToken cancellationToken)
    {
        state.State.Points.Add(point);
        await WriteStateAsync(cancellationToken);
    }

    public async Task AddPointsAsync(IEnumerable<ChartPoint> points)
    {
        State.Points.AddRange(points);

        if (State.Points.Count > 1000)
            State.Points = State.Points.TakeLast(1000).ToList();

        await WriteStateAsync();
    }

    public Task<IReadOnlyList<ChartPoint>> GetPointsAsync(int limit = 500)
    {
        var points = State.Points.TakeLast(limit).ToList();
        return Task.FromResult<IReadOnlyList<ChartPoint>>(points);
    }

    public Task<ChartConfig> GetConfigAsync()
    {
        return Task.FromResult(State.Config);
    }

    public async Task UpdateConfigAsync(ChartConfig config)
    {
        State.Config = config;
        await WriteStateAsync();
    }

    public async Task ClearAsync()
    {
        State.Points.Clear();
        await WriteStateAsync();
    }

    public Task<int> GetPointCountAsync()
    {
        return Task.FromResult(State.Points.Count);
    }
}
