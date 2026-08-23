namespace DigitalBrain.Integrations.Search;

public sealed class NotImplementedWebSearchTransport : IWebSearchTransport
{
    public Task<string> SearchCompanyJsonAsync(string company, CancellationToken cancellationToken)
        => throw new NotImplementedException("Wire MCP later");
}
