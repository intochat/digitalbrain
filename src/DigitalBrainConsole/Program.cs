using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Client;
using DigitalBrainConsole;

await using IDigitalBrain brain = await Brain.CreateAsync(args);

var chat = brain.Get<IChatNeuron>("main");

// Fire twice: the second fire must potentiate the same synapses, not add new ones.
await chat.FireAsync(new UserMessage("hello"));
await chat.FireAsync(new UserMessage("hello again"));

Console.WriteLine();
Console.WriteLine("-- synapses (anatomy) ------------------------------------------");
foreach (var synapse in await brain.GetSynapsesAsync(chat.Id))
{
    Console.WriteLine(synapse);
}

Console.WriteLine();
Console.WriteLine("-- chat:main outgoing journal (physiology) ---------------------");
var journal = await brain.ReadJournalAsync(chat.Id, JournalKind.Outgoing);
foreach (var delivery in journal.Delta)
{
    Console.WriteLine(
        $"#{delivery.Sequence}  {delivery.Signal.GetType().Name}  corr={delivery.CorrelationId}");
}
