using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Scripting.Startup;

internal interface IBehaviorAdmissionSource
{
    IAsyncEnumerable<IReadOnlyList<BehaviorDefinition>> WatchAsync(CancellationToken cancellationToken);

    Task ReportAsync(ReportBehaviorStatus report, CancellationToken cancellationToken);
}
