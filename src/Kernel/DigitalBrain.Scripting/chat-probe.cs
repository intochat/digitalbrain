#:project ../Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj
#:project ../../Modules/UI/DigitalBrain.Modules.UI.Contracts/DigitalBrain.Modules.UI.Contracts.csproj
#:property TreatWarningsAsErrors=false

using DigitalBrain.Abstractions;
using DigitalBrain.Aspire;
using DigitalBrain.Chat;
using DigitalBrain.Client;

var brain = await DigitalBrainClient.ConnectAsync(args);

const string chatName = "scripting-proof";
var command = CommandId.New();
await brain.GetGrainProxy<IChat>(chatName).Send(new SendMessage(command, "Who are you?"));

using var patience = new CancellationTokenSource(TimeSpan.FromMinutes(5));
await foreach (var page in brain.WatchJournalAsync(
    NeuronId.For<IChat>(brain.Owner, chatName), JournalKind.Outgoing, 0, patience.Token))
{
    foreach (var delivery in page.Delta)
    {
        if (delivery.Synapse is Responded response && response.CommandId == command)
        {
            Console.WriteLine(response.Text);
            return;
        }
    }
}

throw new TimeoutException("The brain did not answer the scripting probe within 5 minutes.");
