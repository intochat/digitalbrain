namespace DigitalBrain.Core.Tests;

public sealed record Greet(string Who) : Synapse;

public sealed record Greeted(string Message) : Synapse;

public sealed class Greeter : Neuron, IAnswers<Greet, Greeted>
{
    public Task<Greeted?> HandleAsync(Greet question, CancellationToken cancellationToken)
        => Task.FromResult<Greeted?>(new($"Hello, {question.Who}!"));
}
