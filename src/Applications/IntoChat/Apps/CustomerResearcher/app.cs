#:project /brain/src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/DigitalBrain.Modules.Apps.Contracts.csproj
#:project /brain/src/Modules/DigitalBrain/CustomerResearcher/DigitalBrain.Modules.CustomerResearcher.Contracts/DigitalBrain.Modules.CustomerResearcher.Contracts.csproj
using System.Text.Json;
using DigitalBrain.Apps;
using DigitalBrain.Apps.Signals;
using DigitalBrain.CustomerResearcher;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var appKey = brain.Setting("App")!;
var researcher = brain.Get<ICustomerResearcher>(CustomerResearcherKeys.For(appKey.Split("/packages/")[0]));
var app = brain.Get<IApp>(appKey);

await using var invocations = await brain.SubscribeAsync<AppInvoked>(app, brain.Stopping);
foreach (var missed in await app.Pending()) { await app.Respond(await Answer(missed.Id, missed.Operation, missed.Input)); }
await foreach (var invoked in invocations.ReadAllAsync(brain.Stopping)) { await app.Respond(await Answer(invoked.InvocationId, invoked.Operation, invoked.Input)); }

async Task<AppResponse> Answer(Guid id, string operation, string input)
{
    switch (operation)
    {
        case "open":
            var window = await researcher.OpenWindow();
            return new(id, JsonSerializer.Serialize(new { id = window.Id, title = window.Title, surface = window.Surface.Name }), null);
        case "research":
            if (string.IsNullOrWhiteSpace(input)) { return new(id, null, "Name a company, location or website to research."); }
            await researcher.Research(input);
            return new(id, "Research started.", null);
        case "stop":
            await researcher.Stop();
            return new(id, "Stopped.", null);
        default:
            return new(id, null, $"Unknown operation '{operation}'.");
    }
}
