namespace DigitalBrain.Flutter.Aspire.Hosting;

public sealed class FlutterUiEdgeOptions
{
    public string ResourceName { get; set; } = FlutterHostingExtensions.DefaultUIResourceName;

    public string Owner { get; set; } = FlutterHostingExtensions.DefaultOwner;

    public string? ProjectPath { get; set; }
}
