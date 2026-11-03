using DigitalBrain.Modules.Configuration;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Capabilities.Connections.Connectors;

// Typed accessors over the configuration contract between the AppHost and the Host.
public static class HostConfiguration
{
    public const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public static string? Parameter(this IConfiguration configuration, string name) =>
        configuration.FirstNonWhiteSpace($"Parameters:{name}", name);

    public static string? IntegrationValue(this IConfiguration configuration, string integrationId, string field) =>
        configuration.FirstNonWhiteSpace(IntegrationSchema.Key(integrationId, field));

    public static string? OtlpEndpoint(this IConfiguration configuration) =>
        configuration.FirstNonWhiteSpace(OtlpEndpointVariable);

    public static string? ConnectionString(this IConfiguration configuration, string name) =>
        FirstNonWhiteSpace(configuration.GetConnectionString(name));

    public static string? HostKey(this IConfiguration configuration, string key, string? legacyKey = null) =>
        legacyKey is null
            ? configuration.FirstNonWhiteSpace(key)
            : configuration.FirstNonWhiteSpace(key, legacyKey);

    public static string? Variable(this IConfiguration configuration, string name) =>
        configuration.FirstNonWhiteSpace(name);

    public static bool IsModuleOptionSet(this IConfiguration configuration, string moduleId, string optionName) =>
        configuration.FirstNonWhiteSpace($"{ModuleConfiguration.ModuleOptionsSection(moduleId)}:{optionName}") is not null;

    private static string? FirstNonWhiteSpace(this IConfiguration configuration, params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        foreach (var key in keys)
        {
            if (FirstNonWhiteSpace(configuration[key]) is { } value)
            {
                return value;
            }
        }
        return null;
    }

    private static string? FirstNonWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
