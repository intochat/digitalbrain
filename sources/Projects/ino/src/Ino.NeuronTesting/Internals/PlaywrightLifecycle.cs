using Microsoft.Playwright;

namespace Ino.NeuronTesting.Internals;

// Singleton-per-fixture Playwright + Browser. Lazily created on first
// session.OpenBrowser() call so test classes that never open a browser
// pay zero Chromium cost. Headed locally; headless if CI=true (auto-set
// by every standard CI runner).
public sealed class PlaywrightLifecycle : IAsyncDisposable
{
    IPlaywright? _playwright;
    IBrowser? _browser;
    readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<IBrowserContext> NewContextAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _playwright ??= await Playwright.CreateAsync();
            _browser ??= await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = string.Equals(Environment.GetEnvironmentVariable("CI"),
                    "true", StringComparison.OrdinalIgnoreCase),
            });
        }
        finally { _gate.Release(); }

        return await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }
}
