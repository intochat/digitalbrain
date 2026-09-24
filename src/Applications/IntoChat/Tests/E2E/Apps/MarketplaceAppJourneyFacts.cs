using System.Text.Json;
using DigitalBrain.Flutter;
using IntoChat.Tests.E2E.Workspace;
using Microsoft.Playwright;

namespace IntoChat.Tests.E2E.Apps;

public sealed class MarketplaceAppJourneyFacts
{
    [Fact(Timeout = 600_000)]
    public async Task CreatedAppRunsIndependentlyAfterPublishingAndSwitchingAccounts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await IntoChatE2ETest.Create()
            .ConfigureModule<FlutterModule>(flutter => flutter.RunWebApp()).StartAsync(ct);
        var page = brain.Page;
        page.SetDefaultTimeout(30_000);
        await page.SetViewportSizeAsync(1600, 1100);
        var kernel = brain.HttpClient.BaseAddress ?? throw new InvalidOperationException("Kernel address is missing.");
        var origin = new UriBuilder(kernel) { Host = new Uri(page.Url).Host }.Uri;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var alice = "alice" + suffix;
        var bob = "bob" + suffix;
        const string password = "journey-password-123";
        const string slug = "greeting";
        var appId = alice + "/" + slug;
        const string appName = "Shared greeting";

        await SwitchAndAuthenticateAsync(page, alice, password, register: true);
        var aliceSession = await ReadAsync(page, origin, "/identity/session");
        var aliceWorkspace = aliceSession.GetProperty("workspaceId").GetString()!;
        Assert.Equal(alice, aliceSession.GetProperty("principalId").GetString());
        foreach (var invalidPassword in new[] { "", "definitely-the-wrong-password" })
        {
            var rejected = await page.APIRequest.PostAsync(new Uri(origin, "/identity/login").AbsoluteUri,
                new() { DataObject = new { principalId = alice, password = invalidPassword } });
            Assert.Equal(401, rejected.Status);
        }
        await Button(page, "App studio").ClickAsync();
        await Button(page, "Create").ClickAsync();
        await EnterAsync(page, "App ID", slug);
        await EnterAsync(page, "App name", appName);
        await EnterAsync(page, "Description", "Adds a workspace prefix and converts the greeting to uppercase.");
        await EnterAsync(page, "Default prefix", "Alice ");
        await Button(page, "Create app").ClickAsync();
        await EnterAsync(page, "Input", "world");
        await Button(page, "Run app").ClickAsync();
        await ExpectTextAsync(page, "ALICE WORLD");

        var alicePath = $"/workspaces/{aliceWorkspace}/app-runtime/{appId}";
        var aliceBefore = await ReadAsync(page, origin, alicePath);
        var manifest = aliceBefore.GetProperty("manifest");
        Assert.Equal(appId, manifest.GetProperty("id").GetString());
        Assert.Equal(alice, manifest.GetProperty("publisher").GetString());
        Assert.Equal(2, manifest.GetProperty("composition").GetProperty("parts").GetArrayLength());
        Assert.Equal(2, manifest.GetProperty("composition").GetProperty("bindings").GetArrayLength());
        Assert.Equal("ALICE WORLD", aliceBefore.GetProperty("output").GetString());
        await Button(page, "Publish to marketplace").ClickAsync();
        await ExpectTextAsync(page, $"Published {appId} · 1.0.0");
        await Button(page, "Close apps").ClickAsync();

        await SwitchAndAuthenticateAsync(page, bob, password, register: true);
        var bobSession = await ReadAsync(page, origin, "/identity/session");
        var bobWorkspace = bobSession.GetProperty("workspaceId").GetString()!;
        Assert.NotEqual(aliceWorkspace, bobWorkspace);
        Assert.Equal(bob, bobSession.GetProperty("principalId").GetString());
        var forbidden = await page.APIRequest.GetAsync(new Uri(origin, alicePath).AbsoluteUri);
        Assert.Equal(403, forbidden.Status);
        await Button(page, "App studio").ClickAsync();
        await ExpectTextAsync(page, "No apps installed yet.");
        await Button(page, "Marketplace").ClickAsync();
        var listing = page.GetByRole(AriaRole.Group, new() { Name = $"{appId} · 1.0.0" });
        await Assertions.Expect(listing).ToBeVisibleAsync();
        await listing.GetByRole(AriaRole.Button, new() { Name = "Install", Exact = true }).ClickAsync();
        await EnterAsync(page, "Prefix", "Bob ");
        await Button(page, "Save configuration").ClickAsync();
        await ExpectTextAsync(page, "Configuration saved");
        await EnterAsync(page, "Input", "friend");
        await Button(page, "Run app").ClickAsync();
        await ExpectTextAsync(page, "BOB FRIEND");
        var bobPath = $"/workspaces/{bobWorkspace}/app-runtime/{appId}";
        var bobAfter = await ReadAsync(page, origin, bobPath);
        var foreignPublish = await page.APIRequest.PostAsync(new Uri(origin, bobPath + "/publish").AbsoluteUri);
        Assert.Equal(403, foreignPublish.Status);
        Assert.Equal("1.0.0", bobAfter.GetProperty("manifest").GetProperty("version").GetString());
        Assert.Equal("Bob ", bobAfter.GetProperty("configuration").GetProperty("prefix").GetString());
        Assert.Equal("BOB FRIEND", bobAfter.GetProperty("output").GetString());

        await page.ReloadAsync();
        await Button(page, "App studio").ClickAsync();
        await Button(page, $"{appName} {appId}").ClickAsync();
        await ExpectInputAsync(page, "Prefix", "Bob ");
        await ExpectTextAsync(page, "BOB FRIEND");
        await Button(page, "Close apps").ClickAsync();

        await SwitchAndAuthenticateAsync(page, alice, password, register: false);
        await Button(page, "App studio").ClickAsync();
        await Button(page, $"{appName} {appId}").ClickAsync();
        await ExpectInputAsync(page, "Prefix", "Alice ");
        await ExpectTextAsync(page, "ALICE WORLD");
        var aliceAfter = await ReadAsync(page, origin, alicePath);
        Assert.Equal(aliceBefore.GetProperty("revision").GetInt64(), aliceAfter.GetProperty("revision").GetInt64());
        Assert.Equal(aliceBefore.GetProperty("configuration").GetRawText(), aliceAfter.GetProperty("configuration").GetRawText());
        Assert.Equal(aliceBefore.GetProperty("values").GetRawText(), aliceAfter.GetProperty("values").GetRawText());
        var foreignBob = await page.APIRequest.GetAsync(new Uri(origin, bobPath).AbsoluteUri);
        Assert.Equal(403, foreignBob.Status);
    }

    private static ILocator Button(IPage page, string name) => page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    private static ILocator Textbox(IPage page, string name) => page.GetByRole(AriaRole.Textbox, new() { Name = name, Exact = true });
    private static Task EnterAsync(IPage page, string name, string value) => WorkspaceBrowser.EnterTextAsync(Textbox(page, name), value);
    private static Task ExpectTextAsync(IPage page, string text) => Assertions.Expect(page.GetByText(text, new() { Exact = true })).ToBeVisibleAsync();

    private static async Task ExpectInputAsync(IPage page, string name, string value)
    {
        var input = Textbox(page, name);
        // Flutter populates the native editing element on focus; this reads without changing it.
        await input.ClickAsync();
        await input.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await Assertions.Expect(input).ToHaveValueAsync(value);
    }

    private static async Task SwitchAndAuthenticateAsync(IPage page, string username, string password, bool register)
    {
        await Button(page, "Switch account").ClickAsync();
        if (register) { await Button(page, "Create account").ClickAsync(); }
        await EnterAsync(page, "Username", username);
        await EnterAsync(page, "Password", password);
        await Button(page, register ? "Create account" : "Sign in").ClickAsync();
        await Assertions.Expect(Button(page, "Switch account")).ToBeVisibleAsync();
    }

    private static async Task<JsonElement> ReadAsync(IPage page, Uri origin, string path)
    {
        var response = await page.APIRequest.GetAsync(new Uri(origin, path).AbsoluteUri);
        Assert.Equal(200, response.Status);
        using var json = JsonDocument.Parse(await response.TextAsync());
        return json.RootElement.Clone();
    }
}
