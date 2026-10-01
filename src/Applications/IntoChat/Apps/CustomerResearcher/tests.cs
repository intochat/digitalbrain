#:project /brain/src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/DigitalBrain.Modules.Apps.Contracts.csproj
#:project /brain/src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Contracts/DigitalBrain.Modules.Flutter.Contracts.csproj
#:project /brain/src/Modules/Microsoft/Playwright/DigitalBrain.Modules.Microsoft.Playwright.Contracts/DigitalBrain.Modules.Microsoft.Playwright.Contracts.csproj
#:project /brain/src/Modules/AI/DigitalBrain.Modules.AI.Contracts/DigitalBrain.Modules.AI.Contracts.csproj
using System.Text.Json;
using DigitalBrain.AI.Scripted;
using DigitalBrain.Apps;
using DigitalBrain.Client;
using DigitalBrain.Flutter.Surface;
using DigitalBrain.Microsoft.Playwright;

// The deterministic meaning of the spec: scripted browser and scripted model play the connector,
// the app's own operations drive and inspect it, and nothing here touches a live model or site.
await using var brain = await DigitalBrainClient.ConnectAsync(args);
var package = PackageId.Parse(brain.Setting("Package")!);
var revision = brain.Setting("Revision")!;
var failures = 0;

const string SearchUrl = "https://www.bing.com/search?q=acme";
const string SiteUrl = "https://acme.example/";
const string SiteText = "Acme Robotics builds warehouse robots. Headquarters: Prague, Czechia. Contact: info@acme.example.";
var searchPage = new BrowserObservation(SearchUrl, "acme - Search", "Results for acme",
    [new BrowserLink("Acme Robotics — official site", SiteUrl)]);
var companyPage = new BrowserObservation(SiteUrl, "Acme Robotics", SiteText, []);
const string Extraction = """
    {"status":"found","companyName":"Acme Robotics","website":"https://acme.example","location":"Prague, Czechia",
     "email":"info@acme.example","phone":null,"industry":null,"summary":"Acme Robotics builds warehouse robots."}
    """;

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

await Scenario("Research verifies the company against observed pages and saves it", async (app, scope) =>
{
    await brain.Get<IScriptedBrowser>(scope + "/browser").Script([searchPage, companyPage, companyPage]);
    await brain.Get<IScriptedLLM>(scope + "/model").Script(["""{"linkId":1}""", Extraction]);
    var answer = await Ask(app, "research", "Acme Robotics");
    if (answer != "Saved: Acme Robotics") { throw new InvalidOperationException($"Research answered \"{answer}\"."); }
    var row = JsonDocument.Parse(await Ask(app, "result", "Acme Robotics")).RootElement;
    if (row.ValueKind != JsonValueKind.Object || row.GetProperty("company_name").GetString() != "Acme Robotics"
        || row.GetProperty("email").GetString() != "info@acme.example"
        || !row.GetProperty("evidence").EnumerateArray().Any(item => item.GetProperty("Field").GetString() == "email"))
    { throw new InvalidOperationException("The saved row does not carry the verified company and its evidence."); }
    var requested = await brain.Get<IScriptedBrowser>(scope + "/browser").RequestedUrls();
    if (requested.Length != 2 || !requested[1].StartsWith("https://acme.example", StringComparison.Ordinal))
    { throw new InvalidOperationException("The research did not drive the browser through search and the official site."); }
});

await Scenario("An unverifiable company is refused and saves nothing", async (app, scope) =>
{
    await brain.Get<IScriptedBrowser>(scope + "/browser").Script(
        [new BrowserObservation(SearchUrl, "ghost - Search", "No usable results", []), new BrowserObservation(SearchUrl, "ghost - Search", "No usable results", [])]);
    var answer = await Ask(app, "research", "Ghost Corp");
    if (!answer.StartsWith("Could not select an official company website", StringComparison.Ordinal))
    { throw new InvalidOperationException($"Research answered \"{answer}\"."); }
    if (await Ask(app, "result", "Ghost Corp") != "null") { throw new InvalidOperationException("A refused research saved a row."); }
});

await Scenario("Stop during research cancels it or the research completes, never half-saves", async (app, scope) =>
{
    await brain.Get<IScriptedBrowser>(scope + "/browser").Script([searchPage, companyPage, companyPage]);
    await brain.Get<IScriptedLLM>(scope + "/model").Script(["""{"linkId":1}""", Extraction]);
    var research = await app.Invoke(new InvokeApp(Guid.NewGuid(), "research", "Acme Robotics"));
    await app.Invoke(new InvokeApp(Guid.NewGuid(), "stop", ""));
    var answer = await Await(app, research.Id);
    var saved = await Ask(app, "result", "Acme Robotics");
    // Stop raced a scripted (instant) research: both orders are legal, half-states are not.
    if (answer == "Stopped." ? saved != "null" : answer != "Saved: Acme Robotics" || saved == "null")
    { throw new InvalidOperationException($"Research answered \"{answer}\" but the stored row was {saved}."); }
});

return failures == 0 ? 0 : 1;

async Task Scenario(string name, Func<IApp, string, Task> run)
{
    var scope = $"specs/{package}@{revision}/{Guid.NewGuid():N}";
    var app = brain.Get<IApp>(scope + "/app");
    try
    {
        await app.Install(new InstallApp(Guid.NewGuid(), new(package, revision), new Dictionary<string, string>
        {
            ["Model"] = IScriptedLLM.ModelPrefix + scope + "/model",
            ["Browser"] = "scripted/" + scope + "/browser",
        }));
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
    => await Await(app, (await app.Invoke(new InvokeApp(Guid.NewGuid(), operation, input))).Id);

async Task<string> Await(IApp app, Guid invocationId)
{
    var invocation = await app.ReadInvocation(invocationId);
    var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
    while (invocation.Status == InvocationStatus.Pending)
    {
        if (DateTimeOffset.UtcNow > deadline) { throw new TimeoutException("The app did not answer within 5 minutes."); }
        await Task.Delay(200);
        invocation = await app.ReadInvocation(invocationId);
    }
    if (invocation.Status == InvocationStatus.Failed) { throw new InvalidOperationException(invocation.Error ?? "The app failed."); }
    return invocation.Output ?? "";
}
