namespace DigitalBrain.Shell.Aspire.Hosting;

public sealed class ShellUiEdgeOptions
{
    public string ResourceName { get; set; } = ShellHostingExtensions.DefaultUIResourceName;

    public string Owner { get; set; } = ShellHostingExtensions.DefaultOwner;

    public string? ProjectPath { get; set; }

    // Null lets Aspire proxy the edge on an assigned port; a value pins the host port the
    // product's OAuth callback is registered against, and the edge then listens on it directly.
    public int? HttpPort { get; set; }
}
