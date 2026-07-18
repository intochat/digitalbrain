namespace Brain.Gateway;

public sealed class GatewayFeedOptions
{
    public const string SectionName = "Gateway:Feed";

    public string StreamProviderName { get; set; } = string.Empty;
    public string StreamNamespace { get; set; } = string.Empty;
    public string StreamId { get; set; } = string.Empty;

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(StreamProviderName))
            throw new InvalidOperationException("Gateway:Feed:StreamProviderName is required.");
        if (string.IsNullOrWhiteSpace(StreamNamespace))
            throw new InvalidOperationException("Gateway:Feed:StreamNamespace is required.");
        if (string.IsNullOrWhiteSpace(StreamId) || !Guid.TryParse(StreamId, out _))
            throw new InvalidOperationException("Gateway:Feed:StreamId must be a GUID.");
    }
}
