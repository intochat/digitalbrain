namespace DigitalBrain.Core.V2;

public enum V2RuntimeProfile { Development, Test, Production }

public sealed record V2CapabilityManifest(
    V2RuntimeProfile Profile,
    IReadOnlySet<string> Enabled,
    IReadOnlySet<string> Disabled,
    bool HttpMcpMutations,
    bool TrustedStdioMcp);

public static class V2CapabilityManifests
{
    public static V2CapabilityManifest For(V2RuntimeProfile profile) => profile switch
    {
        V2RuntimeProfile.Development => new(profile, new HashSet<string> { "brain.read", "brain.act", "brain.approve", "brain.admin", "local.ollama", "local.whisper" }, new HashSet<string>(), true, true),
        V2RuntimeProfile.Test => new(profile, new HashSet<string> { "brain.read", "brain.act", "brain.approve", "fake.oauth", "fake.storage" }, new HashSet<string> { "brain.admin", "live.connectors" }, false, false),
        _ => new(profile, new HashSet<string> { "brain.read", "fake-safe" }, new HashSet<string> { "brain.act", "brain.approve", "brain.admin", "trusted.stdio", "local.ollama", "local.whisper" }, false, false),
    };
}
