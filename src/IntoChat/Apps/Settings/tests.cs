#:project /brain/src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/DigitalBrain.Modules.Apps.Contracts.csproj
#:project /brain/src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Contracts/DigitalBrain.Modules.Flutter.Contracts.csproj
using System.Text.Json;
using DigitalBrain.Apps;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Flutter.TextField;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var package = PackageId.Parse(brain.Setting("Package")!);
var revision = brain.Setting("Revision")!;
var failures = 0;

await Scenario("Opening settings composes the surface and answers the defaults", async (app, scope) =>
{
    var payload = JsonDocument.Parse(await Ask(app, "open", "")).RootElement;
    if (payload.GetProperty("id").GetString() != scope + "/app/window" || payload.GetProperty("title").GetString() != "Settings")
    { throw new InvalidOperationException("Settings did not answer the generic window contract."); }
    var surfaceName = payload.GetProperty("surface").GetString()!;
    var surface = await brain.Get<ISurface>(surfaceName).Read();
    if (surface.Definition.Title != "Settings") { throw new InvalidOperationException($"The surface title was \"{surface.Definition.Title}\"."); }
    if (surface.Definition.Children.Length == 0) { throw new InvalidOperationException("The surface has no content."); }
    var preferences = payload.GetProperty("preferences");
    if (preferences.GetProperty("displayName").GetString() != "") { throw new InvalidOperationException("A fresh install has no display name."); }
    if (preferences.GetProperty("theme").GetString() != "system") { throw new InvalidOperationException("A fresh install answers the system theme."); }
});

await Scenario("Typed values are the preferences", async (app, scope) =>
{
    await Ask(app, "open", "");
    await brain.Get<ITextField>(scope + "/app/display-name").SetValue("Ada");
    await brain.Get<ITextField>(scope + "/app/theme").SetValue("dark");

    var preferences = JsonDocument.Parse(await Ask(app, "read", "")).RootElement.GetProperty("preferences");

    if (preferences.GetProperty("displayName").GetString() != "Ada") { throw new InvalidOperationException("The display name was not answered."); }
    if (preferences.GetProperty("theme").GetString() != "dark") { throw new InvalidOperationException("The theme was not answered."); }
});

await Scenario("Preferences survive reopening", async (app, scope) =>
{
    await Ask(app, "open", "");
    await brain.Get<ITextField>(scope + "/app/display-name").SetValue("Ada");
    await brain.Get<ITextField>(scope + "/app/theme").SetValue("light");

    var preferences = JsonDocument.Parse(await Ask(app, "open", "")).RootElement.GetProperty("preferences");

    if (preferences.GetProperty("displayName").GetString() != "Ada" || preferences.GetProperty("theme").GetString() != "light")
    { throw new InvalidOperationException("Reopening lost the preferences."); }
    if ((await brain.Get<ITextField>(scope + "/app/display-name").Read()).Value != "Ada")
    { throw new InvalidOperationException("The field no longer holds the display name."); }
});

return failures == 0 ? 0 : 1;

async Task Scenario(string name, Func<IApp, string, Task> run)
{
    // Scratch installs live inside the host-supplied brain scope, so the scenario's neurons
    // are brain-owned for target authorization instead of failing closed as unclassified.
    var brainScope = brain.Setting("BrainScope");
    var scope = (brainScope is null ? "" : brainScope + "/") + $"specs/{package}@{revision}/{Guid.NewGuid():N}";
    var app = brain.Get<IApp>(scope + "/app");
    try
    {
        await app.Install(new InstallApp(Guid.NewGuid(), new(package, revision), new Dictionary<string, string>()));
        await run(app, scope);
        Console.WriteLine($"dbtest:pass {name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.WriteLine($"dbtest:fail {name}\t{error.Message.ReplaceLineEndings(" ")}");
    }
    finally
    {
        try { await app.Uninstall(new UninstallApp(Guid.NewGuid())); } catch (Exception) { }
    }
}

async Task<string> Ask(IApp app, string operation, string input)
{
    var invocation = await app.Invoke(new InvokeApp(Guid.NewGuid(), operation, input));
    var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
    while (invocation.Status == InvocationStatus.Pending)
    {
        if (DateTimeOffset.UtcNow > deadline) { throw new TimeoutException("The app did not answer within 5 minutes."); }
        await Task.Delay(200);
        invocation = await app.ReadInvocation(invocation.Id);
    }
    if (invocation.Status == InvocationStatus.Failed) { throw new InvalidOperationException(invocation.Error ?? "The app failed."); }
    return invocation.Output ?? "";
}
