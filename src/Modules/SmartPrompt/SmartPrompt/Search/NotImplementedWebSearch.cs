namespace DigitalBrain.SmartPrompt;

internal sealed class NotImplementedWebSearch : IWebSearch
{
    public Task<string> SearchCompanyJsonAsync(string company, CancellationToken cancellationToken)
        => throw new NotImplementedException("Wire MCP later");
}
