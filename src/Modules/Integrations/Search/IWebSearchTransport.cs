namespace DigitalBrain.Integrations.Search;

public interface IWebSearchTransport
{
    Task<string> SearchCompanyJsonAsync(string company, CancellationToken cancellationToken);
}
