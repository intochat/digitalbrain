namespace DigitalBrain.Mocks;

public sealed class MockWebSearch : Neuron, INeuron<WebSearchRequested>
{
    public Task HandleAsync(WebSearchRequested question, CancellationToken cancellationToken)
    {
        var snippet = $"Mock research: {question.Domain} (query={question.Query})";
        var source = $"https://mock.search.test/{question.Domain}";
        Emit(new WebSearchCompleted(question.Query, question.Domain, snippet, source));
        return Task.CompletedTask;
    }
}
