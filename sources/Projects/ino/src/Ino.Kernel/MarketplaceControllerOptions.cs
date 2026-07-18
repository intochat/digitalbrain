namespace Ino.Kernel;

public sealed class MarketplaceControllerOptions
{
    public string MarketplaceFeedPath { get; set; } = Ino.Core.Hosting.InoPaths.MarketplaceJson;
    public string InstalledStatePath { get; set; } = Ino.Core.Hosting.InoPaths.InstalledJson;
    public TimeSpan RestartTimeout { get; set; } = TimeSpan.FromSeconds(60);
}
