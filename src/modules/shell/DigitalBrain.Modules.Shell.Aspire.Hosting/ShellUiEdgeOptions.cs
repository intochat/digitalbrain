namespace DigitalBrain.Shell.Aspire.Hosting;

public sealed class ShellUiEdgeOptions
{
    public string ResourceName { get; set; } = ShellHostingExtensions.DefaultUIResourceName;

    public string Owner { get; set; } = ShellHostingExtensions.DefaultOwner;

    public string? ProjectPath { get; set; }
}
