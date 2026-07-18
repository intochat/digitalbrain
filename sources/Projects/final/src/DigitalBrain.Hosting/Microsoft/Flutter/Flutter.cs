using DigitalBrain.Os.Application;
using DigitalBrain.Os.Infrastructure.Orleans;
using Aspire.Hosting;

namespace DigitalBrain.Hosting.Microsoft.Flutter;

[GrainType(nameof(Flutter))]
public sealed class Flutter : Neuron, IFlutter
{
    private DistributedApplication? DistributedApp =>
        ServiceProvider.GetService(typeof(DistributedApplication)) as DistributedApplication;

    public async Task HandleAsync(StartFlutterClient synapse, CancellationToken cancellationToken)
    {
        var target = string.IsNullOrWhiteSpace(synapse.Target) ? "web-server" : synapse.Target;
        var success = false;
        string message;

        // Select distinct Aspire resource based on target so both web (primary for surfaces) and windows (native) can coexist.
        // "web-server" / "web" / default → flutter-web (auto-booted, no desktop SDK needed).
        // "windows" → flutter-windows (explicit start, requires Windows desktop toolchain).
        string resourceName = target.Equals("windows", StringComparison.OrdinalIgnoreCase) ? "flutter-windows" : "flutter-web";

        var da = DistributedApp;
        if (da is not null)
        {
            var model = da.Services.GetService(typeof(DistributedApplicationModel)) as DistributedApplicationModel;
            var resource = model?.Resources.FirstOrDefault(r =>
                string.Equals(r.Name, resourceName, StringComparison.OrdinalIgnoreCase));

            if (resource is not null)
            {
                var commands = (ResourceCommandService)da.Services.GetService(typeof(ResourceCommandService))!;
                var outcome = await commands.ExecuteCommandAsync(resource, KnownResourceCommands.RestartCommand, cancellationToken);
                if (!outcome.Success)
                {
                    outcome = await commands.ExecuteCommandAsync(resource, KnownResourceCommands.StartCommand, cancellationToken);
                }
                success = outcome.Success;
                message = outcome.Message ?? (success ? resourceName + " resource started via Aspire" : "Aspire command failed");
            }
            else
            {
                message = resourceName + " executable resource not found in DistributedApplication model.";
            }
        }
        else
        {
            var underAppHost = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DIGITALBRAIN_DASHBOARD_URL"));
            if (underAppHost)
            {
                await Emit(new FlutterClientStarted(target, false, resourceName + " is AppHost Aspire resource; start/restart from dashboard or ResourceCommandService."));
                return;
            }
            success = false;
            message = "No DistributedApplication in activation context (standalone without flutter resource); use aspire run ino.cs or dashboard start.";
        }

        await Emit(new FlutterClientStarted(target, success, message));
    }

    public Task StartFlutterClientAsync(string target = "web-server", CancellationToken cancellationToken = default) =>
        Emit(new StartFlutterClient(target));
}