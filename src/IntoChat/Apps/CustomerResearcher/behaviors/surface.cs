#:project /brain/src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/DigitalBrain.Modules.Apps.Contracts.csproj
#:project /brain/src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Contracts/DigitalBrain.Modules.Flutter.Contracts.csproj
using System.Text.Json;
using DigitalBrain.Apps;
using DigitalBrain.Apps.Signals;
using DigitalBrain.Client;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.TextField;
using DigitalBrain.Flutter.WebBrowser;
using DigitalBrain.Flutter.Workspace;

// The researcher's window: edge neurons composed by plain grain calls, safe to re-run. The browser
// part declares its own driver and status text, so no behavior manages the browser session.
await using var brain = await DigitalBrainClient.ConnectAsync(args);
var appKey = brain.Setting("App")!;
var app = brain.Get<IApp>(appKey);

await foreach (var invoked in app.Invocations(brain, brain.Stopping))
{ if (invoked.Operation == "open") { await app.Respond(await OpenAsync(invoked.InvocationId)); } }
return;

async Task<AppResponse> OpenAsync(Guid id)
{
    await ComposeAsync();
    var surface = new UiChildRef(UIVocabulary.SurfaceType, appKey + "/surface");
    var installed = appKey.IndexOf("/packages/", StringComparison.Ordinal);
    var workspace = appKey[..(installed >= 0 ? installed : appKey.LastIndexOf('/', StringComparison.Ordinal))];
    await brain.Get<IWorkspace>(workspace)
        .EnsureOpenAsync("customer-researcher", "Customer Researcher", WindowReference.For(surface), CancellationToken.None);
    return new AppResponse(id, JsonSerializer.Serialize(new { id = "customer-researcher", title = "Customer Researcher", surface = surface.Name }), null);
}

async Task ComposeAsync()
{
    string Name(string part) => appKey + "/" + part;
    UiChildRef Ref(string kind, string part) => new(kind, Name(part));

    await brain.Get<ITextField>(Name("company")).Configure("Company name, location or website", "text", Name("research"));
    await brain.Get<IButton>(Name("research")).Set("Research", "research");
    await brain.Get<IButton>(Name("stop")).Set("Stop", "stop");
    var toolbar = brain.Get<ILayout>(Name("toolbar"));
    await toolbar.Set(new LayoutDefinition("row",
        [Ref(UIVocabulary.TextFieldType, "company"), Ref(UIVocabulary.ButtonType, "research"), Ref(UIVocabulary.ButtonType, "stop"), Ref(UIVocabulary.TextType, "status")],
        Extents: [0, 110, 80, 280]), (await toolbar.Read()).Revision);

    await brain.Get<IWebBrowser>(Name("browser")).Configure(Name("browser"), Name("status"));
    var main = brain.Get<ILayout>(Name("main"));
    await main.Set(new LayoutDefinition("column",
        [Ref(UIVocabulary.LayoutType, "toolbar"), Ref(UIVocabulary.WebBrowserType, "browser")],
        Extents: [64, 0]), (await main.Read()).Revision);

    var surface = brain.Get<ISurface>(Name("surface"));
    await surface.Set(new SurfaceDefinition("Customer Researcher", [Ref(UIVocabulary.LayoutType, "main")]), (await surface.Read()).Revision);
}
