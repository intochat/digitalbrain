namespace DigitalBrain.SmartPrompt;

public interface IWebSearch
{
    Task<string> SearchCompanyJsonAsync(string company, CancellationToken cancellationToken);
}
