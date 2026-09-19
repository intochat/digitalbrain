using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.E2ETesting;

public static class E2EDigitalBrain
{
    public static async Task<E2EApplication> StartAsync<TAppHost>(E2EOptions? options = null, CancellationToken cancellationToken = default)
        where TAppHost : class
    {
        options ??= new();
        var timeout = options.Timeout;
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<TAppHost>(options.Args, cancellationToken)
            .WaitAsync(timeout, cancellationToken);
        builder.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter("Aspire.", LogLevel.Information);
        });
        var app = await builder.BuildAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
        try
        {
            await app.StartAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
            foreach (var resource in options.WaitFor)
            {
                await app.ResourceNotifications.WaitForResourceHealthyAsync(resource, cancellationToken)
                    .WaitAsync(timeout, cancellationToken);
            }
            return new E2EApplication(app);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
