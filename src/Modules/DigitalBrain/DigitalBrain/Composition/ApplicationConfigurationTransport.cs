using System.Text;
using System.Text.Json;

namespace DigitalBrain.Core;

/// <summary>Transports public application settings, never executable objects or credentials.</summary>
public static class ApplicationConfigurationTransport
{
    public const string ConfigurationKey = "DigitalBrain:Testing:Application";
    private const int MaximumBytes = 8 * 1024;

    public static string Write(IApplicationConfiguration application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var modules = ModuleComposition.Resolve(application.Modules);
        ValidatePublicSettings(modules);
        var text = JsonSerializer.Serialize(new Envelope(1,
            modules.Select(m => new Entry(m.Id, new(m.Configuration, StringComparer.OrdinalIgnoreCase))).ToArray()));
        ValidateSize(text);
        return text;
    }

    public static IReadOnlyList<ModuleDefinition> Read(string text, IReadOnlyList<ModuleDefinition> allowedModules)
    {
        ValidateSize(text);
        Envelope envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(text) ?? throw new JsonException(); }
        catch (JsonException) { throw new ArgumentException("Invalid application configuration envelope.", nameof(text)); }
        if (envelope.Version != 1 || envelope.Modules is null)
            { throw new ArgumentException("Unsupported application configuration version.", nameof(text)); }
        var allowed = ModuleComposition.Resolve(allowedModules).ToDictionary(m => m.Id, StringComparer.Ordinal);
        var selected = new Dictionary<string, ModuleDefinition>(StringComparer.Ordinal);
        foreach (var entry in envelope.Modules)
        {
            if (entry is null || entry.Id is null || entry.Settings is null || !allowed.TryGetValue(entry.Id, out var module))
                { throw new ArgumentException("Unknown application module.", nameof(text)); }
            var settings = new Dictionary<string, string?>(entry.Settings, StringComparer.OrdinalIgnoreCase);
            if (!settings.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(module.Configuration.Keys))
                { throw new ArgumentException($"Incomplete or unknown settings for module '{module.Id}'.", nameof(text)); }
            if (!selected.TryAdd(entry.Id, new(module.ModuleType, settings)))
                { throw new ArgumentException("Duplicate application module.", nameof(text)); }
        }
        if (selected.Count != allowed.Count) { throw new ArgumentException("Missing application modules.", nameof(text)); }
        // Transport order cannot reorder dependencies. Reconstruct from the application's trusted graph.
        ModuleDefinition Rebuild(ModuleDefinition module) => new(module.ModuleType,
            selected[module.Id].Configuration, module.Dependencies.Select(Rebuild).ToArray());
        var resolved = ModuleComposition.Resolve(allowedModules.Select(Rebuild).ToArray());
        ValidatePublicSettings(resolved);
        return resolved;
    }

    public static void ValidatePublicSettings(IReadOnlyList<ModuleDefinition> modules)
    {
        foreach (var key in modules.SelectMany(m => m.Configuration.Keys))
        {
            if (string.IsNullOrWhiteSpace(key) || key.Contains("__", StringComparison.Ordinal)
                || key.StartsWith("Orleans:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("DigitalBrain:Testing:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("DigitalBrain:Modules:", StringComparison.OrdinalIgnoreCase))
                { throw new ArgumentException("Module configuration cannot override host-owned settings.", nameof(modules)); }
            if (key.Contains("Secret", StringComparison.OrdinalIgnoreCase) || key.EndsWith("Password", StringComparison.OrdinalIgnoreCase)
                || key.EndsWith("AccessToken", StringComparison.OrdinalIgnoreCase) || key.EndsWith("RefreshToken", StringComparison.OrdinalIgnoreCase))
                { throw new ArgumentException("Credentials require private configuration transport.", nameof(modules)); }
        }
    }

    private static void ValidateSize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes)
            { throw new ArgumentException("Application settings exceed the 8 KiB limit.", nameof(text)); }
    }

    private sealed record Envelope(int Version, Entry[] Modules);
    private sealed record Entry(string Id, Dictionary<string, string?> Settings);
}
