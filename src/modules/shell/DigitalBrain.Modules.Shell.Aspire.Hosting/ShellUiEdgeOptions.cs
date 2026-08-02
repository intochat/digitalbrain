namespace DigitalBrain.Shell.Aspire.Hosting;

public sealed class ShellUiEdgeOptions
{
    public string ResourceName { get; set; } = ShellHostingExtensions.DefaultUIResourceName;

    public string Owner { get; set; } = ShellHostingExtensions.DefaultOwner;

    public string? ProjectPath { get; set; }

    // Null leaves the edge behind the Aspire proxy; a value binds that host port directly.
    public int? HttpPort { get; set; }
}
