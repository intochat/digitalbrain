#:project ../Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj
#:project ../../Modules/UI/DigitalBrain.Modules.UI.Contracts/DigitalBrain.Modules.UI.Contracts.csproj
#:property TreatWarningsAsErrors=false

using DigitalBrain.Aspire;
using DigitalBrain.Client;
using DigitalBrain.UI;

var brain = await DigitalBrainClient.ConnectAsync(args);
await brain.FireAsync(new ChartPoint("cpu", DateTimeOffset.Now.ToString("HH:mm"), 42));

Console.WriteLine(
    "Point fired. Bind (session -> chart) in the synapse graph decides which chart hears it.");
