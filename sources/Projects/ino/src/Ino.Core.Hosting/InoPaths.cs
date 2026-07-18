namespace Ino.Core.Hosting;

public static class InoPaths
{
    public static string InstalledJson => Path.Combine(Home, ".ino", "installed.json");
    public static string MarketplaceJson => Path.Combine(Home, ".ino", "marketplace.json");

    private static string Home =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
