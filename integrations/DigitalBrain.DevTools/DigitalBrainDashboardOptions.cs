using Microsoft.AspNetCore.Builder;

namespace DigitalBrain.DevTools;

public sealed class DigitalBrainDashboardOptions
{
    public bool AllowProduction { get; set; }

    public bool AllowRemoteAccess { get; set; }

    public string? AuthToken { get; set; }

    public string RoutePrefix { get; set; } = "/dashboard";

    public bool HideTrace { get; set; } = true;

    public int CounterUpdateIntervalMs { get; set; } = 1000;

    public int HistoryLength { get; set; } = 100;

    public Action<IEndpointConventionBuilder>? ConfigureEndpoints { get; set; }
}
