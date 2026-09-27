using System.Text.Json;
using DigitalBrain.Contracts;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Microsoft.CSharp;

// What a run is told: where the brain's script edge is, the run's token, its settings and, for a
// triggered run, the signal that started it. Nothing about Orleans reaches the script.
internal sealed class ScriptRunEnvironment(RunTokens tokens, IOptions<CSharpOptions> options, IServiceProvider services)
{
    // Development sandboxes reach the brain on the Docker host.
    internal const string DockerHost = "host.docker.internal";

    public IReadOnlyDictionary<string, string> Create(string fileId, string runId, IReadOnlyDictionary<string, string> settings, Signal? trigger)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DigitalBrain__Edge"] = EdgeUrl(),
            ["DigitalBrain__Token"] = tokens.Issue(fileId, runId),
        };
        foreach (var (name, value) in settings) { environment["CSharpFile__Settings__" + name] = value; }
        if (trigger is not null) { environment["CSharpFile__Trigger"] = JsonSerializer.Serialize(trigger, trigger.GetType(), JsonSerializerOptions.Web); }
        return environment;
    }

    private string EdgeUrl()
    {
        if (options.Value.EdgeUrl is { Length: > 0 } configured) { return configured; }
        var listening = services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>()?.Addresses
            .FirstOrDefault(address => address.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Configure DigitalBrain:CSharp:EdgeUrl; this brain listens on no HTTP address scripts can reach.");
        var wildcard = listening.Replace("://+", "://localhost", StringComparison.Ordinal).Replace("://*", "://localhost", StringComparison.Ordinal)
            .Replace("[::]", "localhost", StringComparison.Ordinal).Replace("0.0.0.0", "localhost", StringComparison.Ordinal);
        return new UriBuilder(wildcard) { Host = DockerHost }.Uri.ToString();
    }
}
