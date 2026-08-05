namespace DigitalBrain.Core.Tests;

public sealed record Greet(string Who) : Synapse<Greeted>;

public sealed record Greeted(string Message) : Synapse;

public sealed class Greeter : Neuron, INeuron<Greet, Greeted>
{
    public Greeted Answer(Greet question) => new($"Hello, {question.Who}!");
}
