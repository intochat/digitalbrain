#:project /brain/src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/DigitalBrain.Modules.Apps.Contracts.csproj
using DigitalBrain.Apps;
using DigitalBrain.Apps.Signals;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var app = brain.Get<IApp>(brain.Setting("App")!);
await using var invocations = await brain.SubscribeAsync<AppInvoked>(app, brain.Stopping);
foreach (var missed in await app.Pending()) { await app.Respond(Count(missed.Id, missed.Input)); }
await foreach (var invoked in invocations.ReadAllAsync(brain.Stopping)) { await app.Respond(Count(invoked.InvocationId, invoked.Input)); }

static AppResponse Count(Guid invocationId, string text)
{
    var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    return new AppResponse(invocationId, words == 1 ? "1 word" : $"{words} words", null);
}
