using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Aspire;

public static class DigitalBrainScriptHost
{
    public static (string Clustering, string Streams) RequireStorage(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var clustering = configuration.GetConnectionString(DigitalBrainNames.Clustering);
        if (string.IsNullOrWhiteSpace(clustering))
        {
            throw new InvalidOperationException(
                "No 'ConnectionStrings:clustering' is configured. Pass "
                + "--ConnectionStrings:clustering \"<azure storage connection>\" (the value the "
                + "running brain's silo uses; see the Aspire dashboard resource environment) or "
                + "export it as ConnectionStrings__clustering.");
        }

        var streams = configuration.GetConnectionString(DigitalBrainNames.Streams);

        return (clustering, string.IsNullOrWhiteSpace(streams) ? clustering : streams);
    }

    extension(DigitalBrainClient)
    {
        public static async Task<IDigitalBrain> ConnectAsync(string[] args, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(args);

            var builder = Host.CreateApplicationBuilder(args);
            var storage = RequireStorage(builder.Configuration);
            builder.Configuration[$"ConnectionStrings:{DigitalBrainNames.Streams}"] = storage.Streams;
            builder.AddDigitalBrainClient();

            var host = builder.Build();
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();

            var brain = host.Services.GetRequiredService<IDigitalBrain>();
            await brain.ActivateAsync(cancellationToken).ConfigureAwait(false);
            return brain;
        }
    }
}
