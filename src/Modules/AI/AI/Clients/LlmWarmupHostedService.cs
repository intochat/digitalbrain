using System.Diagnostics;
using DigitalBrain.AI.Ollama;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.AI;
internal sealed class LlmWarmupHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<LlmWarmupHostedService> logger) : IHostedService
{
    private static readonly TimeSpan WarmBudget = TimeSpan.FromMinutes(3);
    private static readonly ChatMessage WarmPrompt = new(ChatRole.User, " ");
    private static readonly Action<ILogger, string, long, Exception?> LogWarmed =
        LoggerMessage.Define<string, long>(
            LogLevel.Information,
            new EventId(1, nameof(LogWarmed)),
            "Warmed LLM {Model} in {ElapsedMs}ms.");
    private static readonly Action<ILogger, string, Exception?> LogWarmFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(LogWarmFailed)),
            "LLM warmup failed for {Model}; first chat may still pay cold start.");

    private static readonly (Type Key, string ConfiguredModelKey)[] OllamaTargets =
    [
        (typeof(IGemma4), $"{AIClients.ConfigurationRoot}:Ollama:IGemma4:Model"),
    ];

    private CancellationTokenSource? _warmup;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _warmup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = WarmConfiguredModelsAsync(_warmup.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_warmup is null)
        {
            return Task.CompletedTask;
        }

        return _warmup.CancelAsync();
    }

    private async Task WarmConfiguredModelsAsync(CancellationToken cancellationToken)
    {
        foreach (var (key, _) in ConfiguredTargets())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await WarmOneAsync(key, cancellationToken).ConfigureAwait(false);
        }
    }

    private IEnumerable<(Type Key, string ConfiguredModelKey)> ConfiguredTargets()
    {
        foreach (var target in OllamaTargets)
        {
            if (!string.IsNullOrWhiteSpace(configuration[target.ConfiguredModelKey]))
            {
                yield return target;
            }
        }
    }
    private async Task WarmOneAsync(Type modelKey, CancellationToken cancellationToken)
    {
        var name = modelKey.Name;
        try
        {
            var client = services.GetRequiredKeyedService<IChatClient>(modelKey);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(WarmBudget);

            var clock = Stopwatch.StartNew();
            _ = await client.GetResponseAsync([WarmPrompt], cancellationToken: budget.Token).ConfigureAwait(false);
            LogWarmed(logger, name, clock.ElapsedMilliseconds, null);
        }
        catch (Exception failure) when (failure is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            LogWarmFailed(logger, name, failure);
        }
    }
}
