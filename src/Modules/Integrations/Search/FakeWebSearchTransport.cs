namespace DigitalBrain.Integrations.Search;

public sealed class FakeWebSearchTransport : IWebSearchTransport
{
    public Task<string> SearchCompanyJsonAsync(string company, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult("""{"company":"Acme","summary":"Test co"}""");
    }
}
