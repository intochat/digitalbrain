namespace DigitalBrain.Mcp;

internal sealed record RegistryEntry(
    string Identity,
    string Role,
    string? Bundle,
    bool Enabled,
    string? Note,
    DateTimeOffset RegisteredAt);