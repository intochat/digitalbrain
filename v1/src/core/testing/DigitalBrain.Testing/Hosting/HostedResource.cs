namespace DigitalBrain.Testing;

public sealed class HostedResource
{
    private readonly RunningAppHost _host;
    private readonly string _name;

    internal HostedResource(RunningAppHost host, string name)
    {
        _host = host;
        _name = name;
    }

    public Task WaitUntilHealthyAsync(CancellationToken cancellationToken = default)
        => _host.WaitUntilHealthyAsync(_name, cancellationToken);

    public HttpClient CreateHttpClient()
        => _host.CreateHttpClient(_name);
}
