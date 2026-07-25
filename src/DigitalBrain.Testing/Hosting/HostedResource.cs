namespace DigitalBrain.Testing;

public sealed class HostedResource
{
    private readonly RunningAppHost _host;

    internal HostedResource(RunningAppHost host, string name)
    {
        _host = host;
        Name = name;
    }

    public string Name { get; }

    public Task WaitUntilHealthyAsync(
        CancellationToken cancellationToken = default)
        => _host.WaitUntilHealthyAsync(Name, cancellationToken);

    public HttpClient CreateHttpClient(string? endpointName = null)
        => _host.CreateHttpClient(Name, endpointName);
}
