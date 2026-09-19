using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace DigitalBrain.E2ETesting;

public sealed class E2EApplication(DistributedApplication app) : IAsyncDisposable
{
    public DistributedApplication App => app;

    public HttpClient CreateHttpClient(string resourceName) => app.CreateHttpClient(resourceName);

    public Task WaitHealthyAsync(string resourceName, CancellationToken cancellationToken)
        => app.ResourceNotifications.WaitForResourceHealthyAsync(resourceName, cancellationToken);

    public ValueTask DisposeAsync() => app.DisposeAsync();
}
