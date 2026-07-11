namespace DigitalBrain.Core.Runtime;

public enum RuntimeProfile { Development, Test, Production }

public sealed record CapabilityManifest(
    RuntimeProfile Profile,
    IReadOnlySet<string> Enabled,
    IReadOnlySet<string> Disabled,
    bool HttpMcpMutations,
    bool TrustedStdioMcp);

public static class CapabilityManifests
{
    public static CapabilityManifest For(RuntimeProfile profile) => profile switch
    {
        RuntimeProfile.Development => new(profile, new HashSet<string> { "brain.read", "brain.act", "brain.approve", "brain.admin", "local.ollama", "local.whisper" }, new HashSet<string>(), true, true),
        RuntimeProfile.Test => new(profile, new HashSet<string> { "brain.read", "brain.act", "brain.approve", "fake.oauth", "fake.storage" }, new HashSet<string> { "brain.admin", "live.connectors" }, false, false),
        _ => new(profile, new HashSet<string> { "brain.read", "fake-safe" }, new HashSet<string> { "brain.act", "brain.approve", "brain.admin", "trusted.stdio", "local.ollama", "local.whisper" }, false, false),
    };
}

public sealed record RuntimePolicy(
    CapabilityManifest Manifest,
    bool MutationsEnabled,
    bool AdminEnabled,
    IReadOnlyList<Capability> McpCapabilities)
{
    public static RuntimePolicy Resolve(
        RuntimeProfile profile,
        bool mutationsRequested,
        bool adminRequested)
    {
        var manifest = CapabilityManifests.For(profile);
        var mutationsEnabled = mutationsRequested && manifest.HttpMcpMutations;
        var adminEnabled = mutationsEnabled && adminRequested && manifest.Enabled.Contains("brain.admin");
        var capabilities = manifest.Enabled
            .Where(capability => capability == "brain.read" ||
                                 mutationsEnabled && capability is "brain.act" or "brain.approve" ||
                                 adminEnabled && capability == "brain.admin")
            .Select(capability => new Capability(
                capability,
                2,
                Enabled: true,
                RequiresApproval: capability != "brain.read"))
            .OrderBy(static capability => capability.Id, StringComparer.Ordinal)
            .ToArray();
        return new RuntimePolicy(manifest, mutationsEnabled, adminEnabled, capabilities);
    }

    public bool Allows(string capability) =>
        McpCapabilities.Any(candidate =>
            candidate.Enabled && string.Equals(candidate.Id, capability, StringComparison.Ordinal));
}
