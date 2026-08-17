using DigitalBrain.Core.Neurons;

namespace DigitalBrain.Modules.UI.Chart;

public interface IChartEntity : IEntity<ChartState>
{
    Task AddPointAsync(ChartPoint point);
    Task AddPointsAsync(IEnumerable<ChartPoint> points);
    Task<IReadOnlyList<ChartPoint>> GetPointsAsync();
    Task<ChartConfig> GetConfigAsync();
    Task UpdateConfigAsync(ChartConfig config);
    Task ClearAsync();
    Task<int> GetPointCountAsync();
}