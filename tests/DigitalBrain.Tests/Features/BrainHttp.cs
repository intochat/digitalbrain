using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Tests;

internal sealed class BrainHttp : IAsyncDisposable
{
    private readonly WebApplication _app;

    private BrainHttp(WebApplication app)
    {
        _app = app;
        Client = app.GetTestServer().CreateClient();
    }

    public HttpClient Client { get; }

    public static async Task<BrainHttp> StartAsync(BrainSimulation brain, string? username, string? password)
    {
        // WebApplicationFactory<Program> would start a second Orleans silo instead of using the simulation's neurons.
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        if (username is not null && password is not null)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BasicAuthGate.UsernameConfigurationKey] = username,
                [BasicAuthGate.PasswordConfigurationKey] = password,
            });
        }

        builder.Services.AddSingleton(brain.Grains);
        // A 30 s poll means any scenario that sees a stream frame promptly was woken by StreamWake, not by the poll.
        builder.Services.AddSingleton(new SessionStreamOptions { PollInterval = TimeSpan.FromSeconds(30) });
        builder.Services.AddSingleton(brain.SiloServices.GetRequiredService<StreamWake>());
        if (brain.SiloServices.GetService<IUiImageStore>() is { } images)
        {
            builder.Services.AddSingleton(images);
        }
        if (brain.SiloServices.GetService<TableService>() is { } tables)
        {
            builder.Services.AddSingleton(tables);
        }

        var app = builder.Build();
        try
        {
            app.UseBasicAuthGate();
            app.UseSessionNeuron();
            app.MapSurfaceEndpoints();
            app.MapUiEndpoints();
            app.MapActivityEndpoints();
            app.MapGraphEndpoints();
            await app.StartAsync();
            return new BrainHttp(app);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}
