using System.Net.Http.Headers;
using DigitalBrain.Contracts.Edge.V1;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Client;

public static class DigitalBrainClient
{
    // The sandbox supplies the edge and token; the connection owns HTTP and process cancellation.
    public static Task<DigitalBrainConnection> ConnectAsync(string[] args, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables().AddCommandLine(args).Build();
        HttpClient? http = null;
        try
        {
            var edge = configuration[ScriptEdgeProtocol.EdgeSetting] is { Length: > 0 } url ? new Uri(url.TrimEnd('/') + "/")
                : throw new InvalidOperationException($"No script edge is configured; run the script as a C# file, or supply {ScriptEdgeProtocol.EdgeSetting} and {ScriptEdgeProtocol.TokenSetting}.");
            var token = configuration[ScriptEdgeProtocol.TokenSetting] is { Length: > 0 } configured ? configured
                : throw new InvalidOperationException($"{ScriptEdgeProtocol.TokenSetting} is required to call the brain.");
            http = new HttpClient { BaseAddress = edge, Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return Task.FromResult(new DigitalBrainConnection(configuration, http));
        }
        catch
        {
            http?.Dispose();
            (configuration as IDisposable)?.Dispose();
            throw;
        }
    }
}
