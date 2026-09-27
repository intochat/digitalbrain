using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Client;

public static class DigitalBrainClient
{
    // The sandbox hands every run the brain's script edge and a token for that run; the host gives the
    // script configuration and a Stopping token that fires on SIGTERM.
    public static async Task<DigitalBrainConnection> ConnectAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        var edge = builder.Configuration[ScriptEdgeProtocol.EdgeSetting] is { Length: > 0 } url ? new Uri(url.TrimEnd('/') + "/")
            : throw new InvalidOperationException($"No script edge is configured; run the script as a C# file, or supply {ScriptEdgeProtocol.EdgeSetting} and {ScriptEdgeProtocol.TokenSetting}.");
        var token = builder.Configuration[ScriptEdgeProtocol.TokenSetting] is { Length: > 0 } configured ? configured
            : throw new InvalidOperationException($"{ScriptEdgeProtocol.TokenSetting} is required to call the brain.");
        var host = builder.Build();
        var http = new HttpClient { BaseAddress = edge, Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            return new DigitalBrainConnection(host, http);
        }
        catch
        {
            http.Dispose();
            host.Dispose();
            throw;
        }
    }
}
