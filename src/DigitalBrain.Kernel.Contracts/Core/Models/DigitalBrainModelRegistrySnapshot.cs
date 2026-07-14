namespace DigitalBrain.Kernel.Contracts.Models;

using Microsoft.Extensions.Configuration;
public sealed record DigitalBrainRegistryEntry(
    DigitalBrainCapabilityKind Kind,
    string Provider,
    string Id,
    string DisplayName,
    DigitalBrainModelRole Role,
    string ServiceKey,
    DigitalBrainModelCapabilities Capabilities);
public static class DigitalBrainModelRegistrySnapshot
{
    public static IReadOnlyList<DigitalBrainRegistryEntry> Read(IConfiguration config)
    {
        var entries = new List<DigitalBrainRegistryEntry>();
        foreach (var child in config.GetSection("DigitalBrain:ModelRegistry:Registrations").GetChildren())
        {
            if (!Enum.TryParse<DigitalBrainCapabilityKind>(child["Kind"], out var kind))
            {
                continue;
            }
            _ = Enum.TryParse<DigitalBrainModelRole>(child["Role"], out var role);
            entries.Add(new DigitalBrainRegistryEntry(
                kind,
                child["Provider"] ?? string.Empty,
                child["Id"] ?? string.Empty,
                child["DisplayName"] ?? string.Empty,
                role,
                child["ServiceKey"] ?? string.Empty,
                new DigitalBrainModelCapabilities(
                    ParseBool(child["SupportsTools"]),
                    ParseBool(child["SupportsVision"]),
                    ParseBool(child["SupportsStreaming"]),
                    ParseBool(child["SupportsStructuredOutput"]))));
        }
        return entries;
    }
    public static DigitalBrainRegistryEntry? FirstOrDefault(IReadOnlyList<DigitalBrainRegistryEntry> entries, DigitalBrainCapabilityKind kind, Func<DigitalBrainRegistryEntry, bool>? predicate = null)
    {
        foreach (var entry in entries)
        {
            if (entry.Kind == kind && (predicate is null || predicate(entry)))
            {
                return entry;
            }
        }
        return null;
    }
    private static bool ParseBool(string? value) => bool.TryParse(value, out var parsed) && parsed;
}
