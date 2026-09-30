#:project /brain/src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/DigitalBrain.Modules.Apps.Contracts.csproj
#:project /brain/src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Contracts/DigitalBrain.Modules.Flutter.Contracts.csproj
using System.Text.Json;
using DigitalBrain.Apps;
using DigitalBrain.Apps.Signals;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Layout;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.TextField;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var appKey = brain.Setting("App")!;
var app = brain.Get<IApp>(appKey);
var displayName = brain.Get<ITextField>(appKey + "/display-name");
var theme = brain.Get<ITextField>(appKey + "/theme");

await using var invocations = await brain.SubscribeAsync<AppInvoked>(app, brain.Stopping);
foreach (var missed in await app.Pending()) { await app.Respond(await Answer(missed.Id, missed.Operation)); }
await foreach (var invoked in invocations.ReadAllAsync(brain.Stopping)) { await app.Respond(await Answer(invoked.InvocationId, invoked.Operation)); }

async Task<AppResponse> Answer(Guid id, string operation)
{
    if (operation == "open") { await Compose(); }
    var name = (await displayName.Read()).Value;
    var chosen = (await theme.Read()).Value;
    // Preferences are bounded the way the shell renders them: a short name, a known theme.
    var preferences = new
    {
        displayName = name.Length > 120 ? name[..120] : name,
        theme = chosen is "dark" or "light" ? chosen : "system",
    };
    return new(id, JsonSerializer.Serialize(new { surface = appKey + "/surface", preferences }), null);
}

// The fields are the preferences: durable neurons the person edits in place.
async Task Compose()
{
    await displayName.Configure("Display name", "text");
    await theme.Configure("Theme (system, light or dark)", "text");
    var main = brain.Get<ILayout>(appKey + "/main");
    await main.Set(new("column", [new("textfield", appKey + "/display-name"), new("textfield", appKey + "/theme")]),
        (await main.Read()).Revision);
    var surface = brain.Get<ISurface>(appKey + "/surface");
    await surface.Set(new("Settings", [new("layout", appKey + "/main")]), (await surface.Read()).Revision);
}
