namespace DigitalBrain.UI;

// Deliberately no [ClientEntryPoint] here. OwnerBoundCallFilter.IsClientEntryPoint keys off
// the DECLARING type of the interface method being invoked (OwnerBoundCallFilter.cs:134-135),
// so Read() -- declared on the base IEntity<TState>, which DOES carry [ClientEntryPoint] --
// stays reachable to unattributed external callers (GrantChartTools.read_chart and friends),
// while Append -- declared directly here, with no such attribute on IChartEntity -- stays
// unreachable to them. Only an attributed, same-owner grain-to-grain call (ChartNeuron's
// HandleAsync, which passes the owner wall because caller.Owner == target.Owner) can reach it.
[Alias("ui.chart-entity")]
public interface IChartEntity : IEntity<ChartState>
{
    [Alias(nameof(Append))]
    Task Append(ChartStatePoint point, int cap);
}
