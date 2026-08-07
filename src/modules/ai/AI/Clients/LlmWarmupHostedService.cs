using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.AI.Ollama;
using DigitalBrain.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.AI;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Constructed by the generic host DI container via AddHostedService.")]
internal sealed partial class LlmWarmupHostedService(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<LlmWarmupHostedService> logger) : IHostedService
{
    private static readonly TimeSpan WarmBudget = TimeSpan.FromMinutes(3);
    private static readonly ChatMessage WarmPrompt = new(ChatRole.User, " ");

    private static readonly (Type Key, string ConfiguredModelKey)[] OllamaTargets =
    [
        (typeof(Gemma4), $"{AIClients.ConfigurationRoot}:Ollama:Gemma4:Model"),
        (typeof(Llama32), $"{AIClients.ConfigurationRoot}:Ollama:Llama32:Model"),
        (typeof(Qwen35), $"{AIClients.ConfigurationRoot}:Ollama:Qwen35:Model"),
        (typeof(Granite41), $"{AIClients.ConfigurationRoot}:Ollama:Granite41:Model"),
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

        if (!string.IsNullOrWhiteSpace(configuration[$"{AIClients.ConfigurationRoot}:OpenAI:ApiKey"]))
        {
            yield return (typeof(Gpt56), $"{AIClients.ConfigurationRoot}:OpenAI:Gpt56:Model");
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Warmup must never take down the silo; cold start remains a soft failure.")]
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
            LogWarmed(logger, name, clock.ElapsedMilliseconds);
        }
        catch (Exception failure) when (failure is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            LogWarmFailed(logger, failure, name);
        }
    }

    [LoggerMessage(LogLevel.Information, "Warmed LLM {Model} in {ElapsedMs}ms.")]
    private static partial void LogWarmed(ILogger logger, string model, long elapsedMs);

    [LoggerMessage(
        LogLevel.Warning,
        "LLM warmup failed for {Model}; first chat may still pay cold start.")]
    private static partial void LogWarmFailed(ILogger logger, Exception failure, string model);
}
