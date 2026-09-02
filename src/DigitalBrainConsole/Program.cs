using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Client;
using DigitalBrainConsole;

await using IDigitalBrain brain = await Brain.CreateAsync(args);

// "default": tier 1 always addresses the "default" instance of a handler's grain type, so a
// neuron that both handles and broadcasts the same signal type must itself BE that instance —
// otherwise its own "default" sibling activation would also be a tier-1 receiver of this
// broadcast (a real, verified defect: named "main" instead, greeter and logger each received
// and printed twice, and the graph grew a spurious chat -> chat edge).
var chat = brain.Get<IChatNeuron>("default");

// Send twice: the second send must potentiate the same synapses, not add new ones.
await chat.SendAsync(new UserMessageReceived("hello"));
await chat.SendAsync(new UserMessageReceived("hello again"));

Console.WriteLine();
Console.WriteLine("-- synapses (anatomy) ------------------------------------------");
foreach (var synapse in await chat.GetSynapsesAsync())
{
    Console.WriteLine(synapse);
}

Console.WriteLine();
Console.WriteLine("-- chat:default outgoing journal (physiology) ------------------");
var journal = await chat.ReadJournalAsync(JournalKind.Outgoing);
foreach (var delivery in journal.Delta)
{
    Console.WriteLine(
        $"#{delivery.Sequence}  {delivery.Signal.GetType().Name}  corr={delivery.CorrelationId}");
}
