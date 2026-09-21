using DigitalBrain.Core;
using DigitalBrain.Testing.E2E;

namespace DigitalBrain.Flutter;

public static class FlutterE2EConfiguration
{
    public static ModuleConfiguration<FlutterModule> RunWebApp(this ModuleConfiguration<FlutterModule> module,
        Action<BrowserConfiguration> browser)
    {
        BrowserConfiguration.Configure(browser);
        return module.RunWebApp();
    }
}
