#:project /brain/src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/DigitalBrain.Modules.Apps.Contracts.csproj
#:project /brain/src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Contracts/DigitalBrain.Modules.Flutter.Contracts.csproj
using DigitalBrain.Apps;
using DigitalBrain.Client;
using DigitalBrain.Flutter.Button;
using DigitalBrain.Flutter.Button.Signals;
using DigitalBrain.Flutter.Text;
using DigitalBrain.Flutter.TextField;

await using var brain = await DigitalBrainClient.ConnectAsync(args);
var key = brain.Setting("App")!;
var app = brain.Get<IApp>(key);

await Task.WhenAll(ResearchClicks(), StopClicks());

async Task ResearchClicks()
{
    await foreach (var _ in brain.On<ButtonClicked>(brain.Get<IButton>(key + "/research"), brain.Stopping))
    {
        var query = (await brain.Get<ITextField>(key + "/company").Read()).Value.Trim();
        if (query.Length is >= 1 and <= 500)
        { await app.Invoke(new(Guid.NewGuid(), "research", query)); }
        else
        { await brain.Get<IText>(key + "/status").Set("Enter a company name (at most 500 characters)."); }
    }
}

async Task StopClicks()
{
    await foreach (var _ in brain.On<ButtonClicked>(brain.Get<IButton>(key + "/stop"), brain.Stopping))
    { await app.Invoke(new(Guid.NewGuid(), "stop", "")); }
}
