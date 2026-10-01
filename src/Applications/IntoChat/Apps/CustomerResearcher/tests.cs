#:project /brain/src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/DigitalBrain.Modules.Apps.Contracts.csproj
#:project /brain/src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Contracts/DigitalBrain.Modules.Flutter.Contracts.csproj
using System.Text.Json;
using DigitalBrain.Apps;
using DigitalBrain.Flutter.Surface;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var package = PackageId.Parse(brain.Setting("Package")!);
var revision = brain.Setting("Revision")!;
var failures = 0;

await Scenario("Opening the researcher composes its surface", async (app, scope) =>
{
    var window = JsonDocument.Parse(await Ask(app, "open", "")).RootElement;
    if (window.GetProperty("title").GetString() != "Customer Researcher")
    { throw new InvalidOperationException($"The window title was \"{window.GetProperty("title").GetString()}\"."); }
    var surface = await brain.Get<ISurface>(window.GetProperty("surface").GetString()!).Read();
    if (surface.Definition.Title != "Customer Researcher" || surface.Definition.Children.Count == 0)
    { throw new InvalidOperationException("The researcher surface is not composed."); }
});

await Scenario("Stopping an idle researcher is safe", async (app, scope) =>
{
    if (await Ask(app, "stop", "") != "Stopped.") { throw new InvalidOperationException("Stop did not answer."); }
});

return failures == 0 ? 0 : 1;

async Task Scenario(string name, Func<IApp, string, Task> run)
{
    var scope = $"specs/{package}@{revision}/{Guid.NewGuid():N}";
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
