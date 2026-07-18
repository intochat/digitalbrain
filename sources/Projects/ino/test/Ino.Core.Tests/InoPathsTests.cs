using Ino.Core.Hosting;
using Xunit;

namespace Ino.Core.Tests;

public class InoPathsTests
{
    [Fact]
    public void InstalledJson_points_to_home_ino_installed_json()
    {
        var path = InoPaths.InstalledJson;
        Assert.EndsWith(Path.Combine(".ino", "installed.json"), path);
        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void MarketplaceJson_points_to_home_ino_marketplace_json()
    {
        var path = InoPaths.MarketplaceJson;
        Assert.EndsWith(Path.Combine(".ino", "marketplace.json"), path);
        Assert.True(Path.IsPathRooted(path));
    }
}
