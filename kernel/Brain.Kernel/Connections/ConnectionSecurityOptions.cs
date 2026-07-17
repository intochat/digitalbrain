namespace Brain.Kernel.Connections;

public sealed class ConnectionSecurityOptions
{
    public const string SectionName = "Brain:Connections:Security";

    public string? KeyRingPath { get; init; }
    public string? KeyRingBlobUri { get; init; }
}
