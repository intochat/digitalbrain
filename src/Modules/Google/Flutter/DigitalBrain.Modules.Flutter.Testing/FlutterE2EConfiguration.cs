using DigitalBrain.Testing.E2E;

namespace DigitalBrain.Flutter;

public static class FlutterE2EConfiguration
{
    public static FlutterModuleOptions RunWebApp(this FlutterModuleOptions options, Action<BrowserConfiguration> browser)
    {
        BrowserConfiguration.Configure(browser);
        return options.RunWebApp();
    }
}
