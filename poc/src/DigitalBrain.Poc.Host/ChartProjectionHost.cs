using System.Net;
using DigitalBrain.Poc.Charting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Poc.Host;

internal sealed class ChartProjectionHost : IAsyncDisposable
{
    private readonly WebApplication _application;

    private ChartProjectionHost(WebApplication application, Uri baseUri)
    {
        _application = application;
        BaseUri = baseUri;
    }

    public Uri BaseUri { get; }

    public static async Task<ChartProjectionHost> StartAsync(
        TestOwnerAuthority owners,
        ChartProjectionEndpoint charts,
        CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(owners);
        builder.Services.AddSingleton(charts);
        var application = builder.Build();
        application.MapChartProjectionRoutes();
        await application.StartAsync(cancellationToken);
        var address = application.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses
            .SingleOrDefault() ?? throw new InvalidOperationException(
                "The chart projection listener did not publish one address.");
        var baseUri = new Uri(address.EndsWith('/', StringComparison.Ordinal)
            ? address
            : address + "/");
        if (!baseUri.IsLoopback || baseUri.Scheme != Uri.UriSchemeHttp)
        {
            await application.DisposeAsync();
            throw new InvalidOperationException("The chart projection must bind only loopback HTTP.");
        }

        return new ChartProjectionHost(application, baseUri);
    }

    public async ValueTask DisposeAsync()
    {
        await _application.StopAsync();
        await _application.DisposeAsync();
    }
}
