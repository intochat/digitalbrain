using DigitalBrain.Protocol;
using DigitalBrain.Os;
using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Os.Infrastructure.Orleans;
using DigitalBrain.Os.UI;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;

namespace DigitalBrain.Kernel;

// Dedicated WeatherWatcherNeuron: the "new handler" that a weather-watcher bundle (created via ino/LLM create-ino)
// installs on a second kernel. It declares IHandle<WeatherQuery> so after install + activation it participates
// on broadcasts or p2p queries for weather (using real https, mirroring the web_get tool in LlmAgentNeuron).
// This makes the bundle ship/install story produce emergent behavior (a handler that reacts with live data).
public interface IWeatherWatcherNeuron : INeuron, IHandle<WeatherQuery>
{
    Task<WeatherResult?> GetLastResultAsync(CancellationToken cancellationToken = default);
}

[GrainType("weather-watcher")]
public sealed class WeatherWatcherNeuron : Neuron, IWeatherWatcherNeuron
{
    private readonly IServiceProvider _services;

    public WeatherWatcherNeuron(IServiceProvider services)
    {
        _services = services;
    }

    public async Task HandleAsync(WeatherQuery query, CancellationToken cancellationToken)
    {
        // Minimal real https (best practice: prefer IHttpClientFactory from Aspire.Hosting (ex-ServiceDefaults) for resilience).
        // Same endpoints as the LLM web_get tool so the "agent with https searches for weather" is consistent.
        var factory = _services.GetService<IHttpClientFactory>();
        using var http = factory?.CreateClient() ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        string summary;
        string sourceUrl = "https://wttr.in";
        try
        {
            var url = $"https://wttr.in/{Uri.EscapeDataString(query.Location)}?format=3";
            var body = await http.GetStringAsync(url, cancellationToken);
            summary = body.Trim();
        }
        catch (Exception ex)
        {
            summary = $"https error: {ex.Message}";
        }

        var result = new WeatherResult(query.Location, summary, sourceUrl, DateTimeOffset.UtcNow);
        await Emit(result);
        // UI surface now produced by rule in weather-watcher.ino (show card with $ substitution from this result event)

        await Emit(new NeuronTelemetry(Self, "WeatherHttpSearch", new Dictionary<string, string>
        {
            ["location"] = query.Location,
            ["summary"] = summary
        }));
    }

    public async Task<WeatherResult?> GetLastResultAsync(CancellationToken cancellationToken = default)
    {
        var hist = await GetJournalHistoryAsync(10, cancellationToken);
        return hist.OfType<WeatherResult>().LastOrDefault();
    }
}