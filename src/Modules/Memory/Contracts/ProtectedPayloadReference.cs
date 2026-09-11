using System.Text.Json.Serialization;

namespace DigitalBrain.Memory;

[GenerateSerializer]
[Alias("db.protected-payload-reference")]
public sealed record ProtectedPayloadReference
{
    [JsonConstructor]
    public ProtectedPayloadReference(string id, DateTimeOffset? expiresAt)
    {
        if (!Guid.TryParse(id, out _))
        {
            throw new ArgumentException("A protected payload reference must be a Guid.", nameof(id));
        }

        Id = id;
        ExpiresAt = expiresAt;
    }

    [Id(0)] public string Id { get; }

    [Id(1)] public DateTimeOffset? ExpiresAt { get; }
}
