using Orleans.Concurrency;
namespace DigitalBrain.Microsoft.Playwright;

[Alias("playwright.scripted"), Orleans.Metadata.DefaultGrainType("playwright.scripted")]
public interface IScriptedBrowser : IPlaywright
{
    // Starts a fresh scenario; each action consumes one observation, saved before returning.
    Task Script(BrowserObservation[] observations);
    [ReadOnly] Task<string[]> RequestedUrls();
}
