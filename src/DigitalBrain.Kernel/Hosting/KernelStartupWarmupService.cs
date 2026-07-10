using DigitalBrain.Core;
using DigitalBrain.Ino.Context;
using DigitalBrain.Ui.Contracts;

namespace DigitalBrain.Kernel;

// Activates singletons and seeds automations after silo start.
public sealed class KernelStartupWarmupService(
    IGrainFactory grainFactory,
    IHostEnvironment environment,
    ILogger<KernelStartupWarmupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (IsTestMode(environment) || IsV2Runtime())
        {
            return;
        }

        try
        {
            var status = grainFactory.GetGrain<ISystemStatus>("status-main");
            await status.GetTimelineAsync(stoppingToken);
            await grainFactory.GetGrain<IContextNeuron>(IContextNeuron.SingletonKey).GetTimelineAsync(stoppingToken);
            await grainFactory.GetGrain<IDbSupportNeuron>(IDbSupportNeuron.SingletonKey).GetTimelineAsync(stoppingToken);
            await grainFactory.GetGrain<IDataVisualizationNeuron>("chart-main").GetTimelineAsync(stoppingToken);
            await grainFactory.GetGrain<IUserSessionNeuron>(IUserSessionNeuron.SingletonKey).GetTimelineAsync(stoppingToken);
            await grainFactory.GetGrain<IInoNeuron>("ino-main").GetTimelineAsync(stoppingToken);

            // Activate the singleton LLM responder so it subscribes to the timeline at startup.
            await grainFactory.GetGrain<ILlmResponderNeuron>(ILlmResponderNeuron.SingletonKey).GetTimelineAsync(stoppingToken);

            var automation = grainFactory.GetGrain<IAutomationNeuron>("automation-main");
            await automation.GetTimelineAsync(stoppingToken);

            // ScheduleTriggerNeuron warms from activation side effects.
            await grainFactory.GetGrain<IScheduleTriggerNeuron>("schedule-main").GetTimelineAsync(stoppingToken);

            await automation.DefineReactionAsync(
                "auto-brief-on-activation",
                "NeuronActivated",
                null,
                "return new[] { new ListSurface(\"AutomationBrief\", new[] { \"System activated - lightweight reactions live\", \"Use MCP list_automations or define more\" }) };",
                cancellationToken: stoppingToken);

            await automation.DefineReactionAsync(
                "signal-context-reactor",
                "Signal:DailyBriefRequested",
                null,
                "var name = (input as Signal)?.Payload?.GetValueOrDefault(\"neuron\")?.ToString() ?? \"brain\"; return new[] { new Signal(\"DailyBriefGenerated\", new Dictionary<string,object?> { [\"source\"] = \"automation\", [\"neuron\"] = name }) };",
                cancellationToken: stoppingToken);

            await automation.FireAsync(new RegisterScript("shared.brief-gen", "return new[] { new Signal(\"SharedBriefEmitted\", new Dictionary<string,object?> { [\"reused\"] = true }) };", "Reusable brief emitter", Array.Empty<string>(), "default"), stoppingToken);
            await automation.FireAsync(new RegisterReaction("brief-on-pa-activate", "NeuronActivated", "shared.brief-gen", "personal-assistant", Array.Empty<string>(), "default", null), stoppingToken);
            await automation.FireAsync(new RegisterReaction("brief-on-any-activate", "NeuronActivated", "shared.brief-gen", null, Array.Empty<string>(), "default", null), stoppingToken);

            await automation.FireAsync(new RegisterScript("scoped.demo", "return new[] { new Signal(\"ScopedOnly\", null) };", "scoped only", Array.Empty<string>(), "demo-user"), stoppingToken);
            await automation.FireAsync(new RegisterReaction("scoped-reaction", "NeuronActivated", "scoped.demo", null, Array.Empty<string>(), "demo-user", null), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kernel startup neuron warmup failed.");
        }
    }

    private static bool IsTestMode(IHostEnvironment environment) =>
        string.Equals(Environment.GetEnvironmentVariable("DIGITALBRAIN_TEST_MODE"), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase);

    private static bool IsV2Runtime() =>
        string.Equals(Environment.GetEnvironmentVariable("DigitalBrain__Runtime"), "V2", StringComparison.OrdinalIgnoreCase);
}
