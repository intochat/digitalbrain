using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Core.Registry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace DigitalBrain.Testing.Unit;

public static class UnitTest
{
    public static UnitTestBuilder Create() => new();
    internal static async Task<UnitBrain> StartAsync(UnitOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        options.Execution.Validate();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Execution.StartupTimeout);
        deadline.Token.ThrowIfCancellationRequested();
        var modules = ModuleComposition.Resolve(options.Modules);
        ModuleSettingsValidation.ValidatePublicSettings(modules);
        var builder = new InProcessTestClusterBuilder(1);
        builder.Options.ConfigureFileLogging = false;
        builder.ConfigureHost(host =>
        {
            host.Logging.SetMinimumLevel(LogLevel.Warning);
            foreach (var definition in modules)
            {
                host.Configuration.AddInMemoryCollection(definition.Configuration);
            }
            host.Configuration.AddInMemoryCollection(options.Execution.PrivateConfiguration);
        });
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddDigitalBrain().AddNeuronRegistry(modules.Select(module => module.ModuleType));
            foreach (var module in modules) { module.Configure(silo); }
            silo.AddMemoryGrainStorage("Default");
            if (options.UseReminders) { silo.UseInMemoryReminderService(); }
            options.ConfigureSilo?.Invoke(silo);
        });
        builder.ConfigureClient(client => { client.AddDigitalBrain(); options.ConfigureClient?.Invoke(client); });
        InProcessTestCluster? cluster = null;
        var lifetime = new TestSessionLifetime(options.Execution);
        try
        {
            cluster = builder.Build();
            lifetime.Own("cluster", cluster);
            await cluster.DeployAsync(deadline.Token).ConfigureAwait(false);
            return new UnitBrain(cluster, options.Execution, lifetime);
        }
        catch (Exception error)
        {
            try { await lifetime.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { error.Data["StartupRollbackFailure"] = cleanup; }
            throw;
        }
    }
}
