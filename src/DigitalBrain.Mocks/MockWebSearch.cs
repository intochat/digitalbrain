namespace DigitalBrain.Mocks;

// Directed research ask/answer — no network; deterministic snippets for journal proofs.
public sealed record WebSearchRequested(string Query, string Domain) : Synapse;

public sealed record WebSearchCompleted(
    string Query,
    string Domain,
    string Snippet,
    string Source) : Synapse;

public sealed class MockWebSearch : Neuron, IAnswers<WebSearchRequested, WebSearchCompleted>
{
    public Task<WebSearchCompleted?> HandleAsync(
        WebSearchRequested question, CancellationToken cancellationToken)
    {
        var snippet = $"Mock research: {question.Domain} (query={question.Query})";
        var source = $"https://mock.search.test/{question.Domain}";
        return Task.FromResult<WebSearchCompleted?>(
            new WebSearchCompleted(question.Query, question.Domain, snippet, source));
    }
}
