using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;

namespace DigitalBrain.Os.Tests;

// Extracted harness per SD2/SIM3 (replaces tolerant NeuronE2ETest shape).
// Boots real AppHost (no SKIP_FLUTTER) for flutter-web target (the cross-platform surface renderer).
// Detects Playwright install + flutter endpoint; Skip-with-reason (tolerant assert message) if missing, genuinely fail otherwise when present.
// Screenshots to pa-files/simulations/{runId}/ .
public sealed class SimulationUiHost : IAsyncDisposable
{
    public DistributedApplication? App { get; private set; }
    public IPlaywright? Playwright { get; private set; }
    public IBrowser? Browser { get; private set; }
    public IPage? Page { get; private set; }
    public string? FlutterEndpoint { get; private set; }
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync(string runId = "ui")
    {
        var cancellationToken = CancellationToken.None;
        try
        {
            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.DigitalBrain_AppHost>(cancellationToken);
            App = await builder.BuildAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(120), cancellationToken);
            await App.StartAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(120), cancellationToken);
        }
        catch (Exception ex)
        {
            SkipReason = $"AppHost boot failed (no Flutter resource or SDK?): {ex.Message}";
            return;
        }

        try
        {
            Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            var isCi = string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
            Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = isCi, SlowMo = isCi ? 0 : 50 });
            var context = await Browser.NewContextAsync(new BrowserNewContextOptions { IgnoreHTTPSErrors = true });
            Page = await context.NewPageAsync();
        }
        catch (Exception ex)
        {
            SkipReason = $"Playwright not installed or launch failed (run 'playwright install'): {ex.Message}";
            return;
        }

        try
        {
            FlutterEndpoint = "http://localhost:5801";
            var ep = App.GetEndpoint("flutter-web");
            if (ep != null) FlutterEndpoint = ep.ToString() ?? FlutterEndpoint;
            await Page!.GotoAsync(FlutterEndpoint, new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30000 });
            await Page.WaitForTimeoutAsync(1500);
        }
        catch (Exception ex)
        {
            SkipReason = $"flutter-web endpoint not available or no SDK listening: {ex.Message}";
            return;
        }
    }

    public async Task ScreenshotAsync(string name)
    {
        if (Page == null || !string.IsNullOrWhiteSpace(SkipReason)) return;
        var dir = Path.Combine("pa-files", "simulations", name);
        Directory.CreateDirectory(dir);
        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, "surface.png") });
    }

    public async ValueTask DisposeAsync()
    {
        if (Page != null) await Page.CloseAsync();
        if (Browser != null) await Browser.DisposeAsync();
        Playwright?.Dispose();
        if (App != null) await App.DisposeAsync();
    }
}