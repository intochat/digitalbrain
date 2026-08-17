namespace DigitalBrain.Mcp;

internal sealed record BundleInstallResult(
    string Name,
    int MemberCount,
    int WireCount,
    bool Enabled);