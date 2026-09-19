using DigitalBrain.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;

namespace DigitalBrain.E2ETesting;

public sealed class ModuleWebHost : IAsyncDisposable
{
    private readonly WebApplication _web;

    private ModuleWebHost(WebApplication web, HttpClient http)
    {
        _web = web;
        Http = http;
    }

    public HttpClient Http { get; }

    public static async Task<ModuleWebHost> StartAsync(
        IClusterClient cluster, IEnumerable<IModule> modules, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(modules);
        var webBuilder = WebApplication.CreateBuilder();
        webBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        webBuilder.Logging.ClearProviders();
        webBuilder.Services.AddSingleton<IGrainFactory>(cluster);
        webBuilder.Services.AddSingleton<IClusterClient>(cluster);
        var web = webBuilder.Build();
        foreach (var module in modules) { module.Configure(web); }
        await web.StartAsync(cancellationToken).ConfigureAwait(false);
        var http = new HttpClient { BaseAddress = new Uri(web.Urls.Single() + "/") };
        return new(web, http);
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _web.DisposeAsync().ConfigureAwait(false);
    }
}
