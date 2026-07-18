namespace DigitalBrain.Aspire.Hosting;

public sealed record DigitalBrainSiloOptions
{
    public string? WorldId { get; init; }
    public string? ClusterId { get; init; }
    public string ServiceId { get; init; } = "digitalbrain";
    public int? SiloPort { get; init; }
    public int? GatewayPort { get; init; }
    public System.Net.IPAddress? AdvertisedIPAddress { get; init; }
    public bool UseInMemoryReminders { get; init; } = true;
    // Enable the official Orleans dashboard on the kernel's existing Aspire "http" endpoint.
    // Default true so it starts with every digitalbrain kernel and is discoverable via IAspire + DIGITALBRAIN_ORLEANS_DASHBOARD_URL.
    // Per 5-steps: no extra ports, rides existing endpoint for simplicity.
    public bool EnableOrleansDashboard { get; init; } = true;
    // Future expansion (models, clustering provider) kept out for phase 1 minimal wiring unification.
}
