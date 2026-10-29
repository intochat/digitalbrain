using System.Text.RegularExpressions;
using DigitalBrain;

namespace MODULE_NAMESPACE.Tests.Unit;

public sealed class MODULE_CLASSModuleFacts
{
    [Fact]
    public void Manifest_declares_the_needs()
    {
        var manifest = ModuleManifest.Of(typeof(MODULE_CLASSModule));

        Assert.Equal("MODULE_ID", manifest.Id);
        Assert.Equal([Need.State("state")], manifest.Needs);
        Assert.Equal(typeof(MODULE_CLASSOptions), Assert.Single(manifest.Settings).OptionsType);
    }

    [Fact]
    public void Module_id_is_vendor_dot_name()
    {
        Assert.Matches(new Regex("^[a-z][a-z0-9-]*(\\.[a-z][a-z0-9-]*)+$"), MODULE_CLASSModule.Id);
    }
}
