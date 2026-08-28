namespace DigitalBrain.Integrations.Search;

public sealed class FakeWebSearchTransport : IWebSearchTransport
{
    public Task<string> SearchCompanyJsonAsync(string company, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var intochat = company.Contains("intochat", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(intochat
            ? """{"company":"IntoChat","website":"https://intochat.io","summary":"IntoChat builds AI customer conversation software.","verified":true}"""
            : """{"company":"Acme","website":"https://acme.test","summary":"Test company.","verified":true}""");
    }
}
