namespace Aspire.Hosting.TripRadar;

internal sealed class TripRadarOptions
{
    public string EnvironmentName { get; set; } = "Development";

    public bool MockExternalApis { get; set; }

    public bool SkipElasticsearch { get; set; } = true;
}
