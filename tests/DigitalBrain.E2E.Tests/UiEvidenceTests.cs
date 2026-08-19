using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Testing.E2E;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace DigitalBrain.E2E.Tests;

// Env-gated so the ungated suite run never pays for a second AppHost boot, a Flutter web
// compile, or a Playwright browser download: default posture is Assert.Skip. See
// UiEvidenceCollection.cs for why this class joins a SECOND, non-shared collection instead of
// E2ECollection.
[Collection(UiEvidenceCollection.Name)]
public sealed class UiEvidenceTests
{
    private const string GateEnvironmentVariable = "DIGITALBRAIN_UI_EVIDENCE";

    // ShellHostingExtensions.DefaultFlutterResourceName duplicated per this assembly's
    // established pattern for internal AppHost-side constants (see McpSurfaceTests' tool names).
    private const string FlutterResourceName = "flutter";

    // Flutter's web dev server (flutter_tools' ResidentWebRunner) prints this banner once it
    // starts serving. It is the only place the randomly-assigned port is observable: shell/web
    // carries no Aspire HTTP endpoint of its own (ShellHostingExtensions never registers one for
    // the "flutter" resource), and the dev server binds a random port unless a
    // web_dev_config.yaml or --web-port pins one (neither is wired here).
    //
    // CONFIRMED LIVE (task-4-report.md gated attempts): ShellHostingExtensions.WithWebHost(),
    // as AppHost.cs calls it (no configure callback), hardcodes FlutterHostOptions.DeviceTarget
    // to ShellNames.DefaultWebDeviceTarget = "chrome" -- Flutter's OWN visible, tool-launched
    // Chrome, not the headless "web-server" device Flutter's own docs recommend for automated
    // testing (flutter.dev/testing/integration-tests: `-d web-server`). The "chrome" device
    // never prints this banner at all (observed through a full successful launch -- dependency
    // resolution, compile, Dart VM Service connect, "Flutter run key commands" -- with zero such
    // line), so this regex can only ever match if the device target is "web-server". Changing
    // the device target requires editing ShellHostingExtensions/AppHost.cs, both out of this
    // task's file scope (test files only); see task-4-report.md for the full finding.
    private static readonly Regex ServedAtPattern = new(
        @"is being served at (?<url>https?://\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public async Task ShellRendersFirstFrameAndScreenshots()
    {
        if (Environment.GetEnvironmentVariable(GateEnvironmentVariable) is null)
        {
            Assert.Skip($"UI evidence is env-gated; set {GateEnvironmentVariable}=1");
        }

        var cancellationToken = TestContext.Current.CancellationToken;

        // Never the shared E2ECollection fixture -- AppHost:UiHost=web selects the web shell
        // (AppHost.cs:27-38), which the classic collection's boot never sets, and this fixture
        // boots and disposes entirely inside this test body.
        var fixture = new BrainAppHostFixture<Projects.DigitalBrain_AppHost>(new BrainE2EOptions
        {
            Args = ["--AppHost:UiHost=web"],
            ExplicitStart = ["ollama", "openwebui"], // never "flutter" -- this leg needs it running
            ExpectedHealthy = ["kernel", "mcp", FlutterResourceName],
            HealthTimeout = TimeSpan.FromMinutes(3),
        });

        try
        {
            await fixture.InitializeAsync();

            var shellUrl = await DiscoverShellUrlAsync(fixture.App, TimeSpan.FromSeconds(45), cancellationToken);

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
            });
            var page = await browser.NewPageAsync();
            await page.GotoAsync(shellUrl, new PageGotoOptions { Timeout = 60_000 });

            // First-frame signal: the Flutter web engine mounts <flt-glass-pane> as the host
            // element for the canvas/DOM once the first frame renders. shell/web/index.html
            // carries the unmodified default flutter_bootstrap.js loader (no custom "onEntry
            // point"/first-frame script), so this framework-level DOM marker is the reliable
            // signal rather than an app-authored event.
            await page.WaitForSelectorAsync(
                "flt-glass-pane", new PageWaitForSelectorOptions { Timeout = 60_000 });

            var screenshotPath = Path.Combine(AppContext.BaseDirectory, "ui-evidence", "shell.png");
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath });

            Assert.True(File.Exists(screenshotPath), $"Screenshot did not land at {screenshotPath}.");
            Assert.True(new FileInfo(screenshotPath).Length > 0, "Screenshot file is empty.");
            Assert.Equal("DigitalBrain", await page.TitleAsync());
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    // The dedicated fixture registers no Aspire endpoint for "flutter" (see the field comment
    // above), so the served URL is recovered from the resource's own log stream -- the same
    // ResourceLoggerService BrainAppHostFixture's internal ResourceLogCollector already taps for
    // its unhealthy-resource diagnostics. Subscribing starts only after InitializeAsync returns
    // (flutter's Aspire "Running" state, which drives that wait absent a registered health
    // check, fires as soon as the process spawns -- well before the multi-second web compile
    // finishes and this banner prints), so the race with the banner is minimal in practice.
    private static async Task<string> DiscoverShellUrlAsync(
        DistributedApplication app, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var loggerService = app.Services.GetRequiredService<ResourceLoggerService>();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await foreach (var batch in loggerService.WatchAsync(FlutterResourceName)
                               .WithCancellation(timeoutSource.Token))
            {
                foreach (var line in batch)
                {
                    var match = ServedAtPattern.Match(line.Content);
                    if (match.Success)
                    {
                        return match.Groups["url"].Value;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        throw new TimeoutException(
            $"The '{FlutterResourceName}' resource never printed its served-at URL within {timeout}.");
    }
}
