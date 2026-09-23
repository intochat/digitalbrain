using Microsoft.Extensions.Configuration;

namespace IntoChat.ServiceDefaults;

public sealed class ServiceTelemetryOptions
{
    public const string SectionName = "Telemetry:Tracing";
    public double? SampleRatio { get; set; }
}

// Standard OpenTelemetry keys and the module-specific privacy override stay at this
// composition boundary. The SDK still reads its own exporter configuration.
internal sealed class TelemetryIntegrationOptions
{
    [ConfigurationKeyName("OTEL_EXPORTER_OTLP_ENDPOINT")]
    public string? ExporterEndpoint { get; set; }

    [ConfigurationKeyName("DigitalBrain:AI:Telemetry:EnableSensitiveData")]
    public bool? EnableSensitiveData { get; set; }
}