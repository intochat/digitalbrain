using Aspire.Hosting.Testing;
using DigitalBrain.Testing.E2E;
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
    private const string PlaywrightBrowsersPathVariable = "PLAYWRIGHT_BROWSERS_PATH";
    private const string RepoRootMarkerFile = "DigitalBrain.slnx";
    private const string PlaywrightBrowsersDirectoryName = ".playwright";

    // ShellHostingExtensions.DefaultFlutterResourceName / ShellNames.HttpEndpointName duplicated
    // per this assembly's established pattern for internal AppHost-side constants (see
    // McpSurfaceTests' tool names).
    private const string FlutterResourceName = "flutter";
    private const string ShellHttpEndpointName = "http";

    [Fact]
    public async Task ShellRendersFirstFrameAndScreenshots()
    {
        if (Environment.GetEnvironmentVariable(GateEnvironmentVariable) is null)
        {
            Assert.Skip($"UI evidence is env-gated; set {GateEnvironmentVariable}=1");
        }

        // Browser binaries must land in the repo-local, git-ignored .playwright directory, never
        // under the user profile (Playwright's default cache). Set before
        // CaptureShellEvidenceAsync is invoked: Microsoft.Playwright types load only when that
        // method first runs, so no Playwright code observes the environment before this line.
        Environment.SetEnvironmentVariable(
            PlaywrightBrowsersPathVariable, ResolvePlaywrightBrowsersPath());

        // Never the shared E2ECollection fixture -- AppHost:UiHost=web selects the web shell
        // (AppHost.cs:27-38), which the classic collection's boot never sets, and this fixture
        // boots and disposes entirely inside this test body.
        var fixture = new BrainAppHostFixture<Projects.DigitalBrain_AppHost>(new BrainE2EOptions
        {
            Args = ["--AppHost:UiHost=web"],
            ExplicitStart = ["ollama", "openwebui"], // never "flutter" -- this leg needs it running
            ExpectedHealthy = ["kernel", "mcp", FlutterResourceName],
            // The flutter resource now carries WithHttpHealthCheck("/") on its fixed endpoint
            // (ShellHostingExtensions), so healthy means the web dev server is up and answering
            // (the release build itself may still be in flight for a few more seconds --
            // CaptureShellEvidenceAsync's reload fallback covers that window). Five minutes
            // bounds the wait honestly even on a cold flutter cache; kernel and mcp share the
            // budget but are ready in well under one.
            HealthTimeout = TimeSpan.FromMinutes(5),
        });

        try
        {
            await fixture.InitializeAsync();

            // The flutter resource exposes a real unproxied HTTP endpoint
            // (ShellNames.FlutterWebPort, the same values FlutterHostLaunch pins into flutter
            // run's --web-port/--web-hostname), so the served address is an ordinary endpoint
            // lookup -- Task 4's log-scrape for Flutter's served-at banner is gone along with
            // the endpoint gap that forced it.
            var shellUrl = fixture.App.GetEndpoint(FlutterResourceName, ShellHttpEndpointName);

            await CaptureShellEvidenceAsync(shellUrl);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    // Kept out of the test body so Microsoft.Playwright's types load only once this method is
    // invoked -- after PLAYWRIGHT_BROWSERS_PATH is set (the JIT resolves a method's referenced
    // types when the method itself first runs, not when its caller does).
    private static async Task CaptureShellEvidenceAsync(Uri shellUrl)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
        var page = await browser.NewPageAsync();
        await page.GotoAsync(shellUrl.ToString(), new PageGotoOptions { Timeout = 60_000 });

        try
        {
            await WaitForFirstFrameAsync(page);
        }
        catch (TimeoutException)
        {
            // Flutter's web dev server answers "/" -- and therefore the Aspire health check --
            // within seconds of launch, up to ~30s before the release build actually lands
            // (observed live: first 200 at launch+3s, "Built build\web" at launch+21s). A
            // navigation inside that window loads a stale or app-less document. One reload
            // after the first bounded wait deterministically lands past the build; the
            // web-server target serves a release build (plain static script bootstrap, no
            // one-shot DWDS debug handshake), so reloading is always safe.
            await page.ReloadAsync(new PageReloadOptions { Timeout = 60_000 });
            await WaitForFirstFrameAsync(page);
        }

        var screenshotPath = Path.Combine(AppContext.BaseDirectory, "ui-evidence", "shell.png");
        Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath });

        Assert.True(File.Exists(screenshotPath), $"Screenshot did not land at {screenshotPath}.");
        Assert.True(new FileInfo(screenshotPath).Length > 0, "Screenshot file is empty.");
        Assert.Equal("DigitalBrain", await page.TitleAsync());
    }

    // First-frame signal: the Flutter web engine mounts <flt-glass-pane> (inside
    // <flutter-view>) once the app boots its view. shell/web/index.html carries the unmodified
    // default flutter_bootstrap.js loader (no custom "onEntry point"/first-frame script), so
    // this framework-level DOM marker is the reliable signal rather than an app-authored event.
    // State must be Attached: the glass pane is a zero-size host whose actual rendering lives
    // in its shadow root, so Playwright's default Visible state never matches it even on a
    // fully rendered page (proven live against Flutter 3.44.8 -- glass pane present in the DOM
    // and WebGL frames painted, Visible wait still timing out).
    private static Task WaitForFirstFrameAsync(IPage page)
        => page.WaitForSelectorAsync(
            "flt-glass-pane",
            new PageWaitForSelectorOptions
            {
                State = WaitForSelectorState.Attached,
                Timeout = 60_000,
            });

    private static string ResolvePlaywrightBrowsersPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, RepoRootMarkerFile)))
            {
                return Path.Combine(directory.FullName, PlaywrightBrowsersDirectoryName);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root ({RepoRootMarkerFile}) above '{AppContext.BaseDirectory}', "
            + "so the repo-local Playwright browsers directory cannot be resolved.");
    }
}
