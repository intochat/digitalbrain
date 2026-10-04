using DigitalBrain;
using DigitalBrain.Client;
using DigitalBrain.Client.Orleans;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace DigitalBrain.Testing.Module;

public static class ModuleTest
{
    public static ModuleTestBuilder Create() => new();
    internal static async Task<ModuleBrain> StartAsync(ModuleOptions? options = null, CancellationToken cancellationToken = default)
    {
        Orleans.Runtime.RequestContext.Remove(DigitalBrain.Kernel.Enforcement.CallerContextStamper.RequestContextKey);
        options ??= new();
        options.Execution.Validate();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.Execution.StartupTimeout);
        deadline.Token.ThrowIfCancellationRequested();
        var modules = ModuleComposition.Resolve(options.Modules);
        ModuleSettingsValidation.ValidatePublicSettings(modules);
        var builder = new InProcessTestClusterBuilder(1);
        builder.Options.ConfigureFileLogging = false;
        // The platform refuses to start without a master key or a declared auth posture; each
        // cluster gets a throwaway key and the Open test posture, shared with the HTTP edge so
        // both sides of the brain protect with the same key. A fact that needs Secured overrides
        // the posture through its PrivateConfiguration.
        var masterKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        builder.ConfigureHost(host =>
        {
            host.Configuration.AddInMemoryCollection(TestLogging.QuietDefaults);
            host.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DigitalBrainNames.MasterKeyConfigurationKey] = masterKey,
                ["DigitalBrain:Auth:Posture"] = "Open",
            });
            foreach (var definition in modules)
            {
                host.Configuration.AddInMemoryCollection(definition.Configuration);
            }
            host.Configuration.AddInMemoryCollection(options.Execution.PrivateConfiguration);
        });
        // The HTTP edge needs to know which parameter types are silo services when it builds
        // endpoint delegates, so the composed collection's service types are snapshotted here.
        HashSet<Type> siloServiceTypes = [];
        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddDigitalBrain(modules.Select(module => module.ModuleType));
            silo.Services.AddDigitalBrainClient();
            silo.AddDigitalBrainPlatform();
            foreach (var module in modules) { module.Configure(silo); }
            silo.AddMemoryGrainStorage("Default");
            if (options.UseReminders) { silo.UseInMemoryReminderService(); }
            options.ConfigureSilo?.Invoke(silo);
            foreach (var descriptor in silo.Services) { siloServiceTypes.Add(descriptor.ServiceType); }
        });
        builder.ConfigureClient(client => { client.AddDigitalBrain(); options.ConfigureClient?.Invoke(client); });
        InProcessTestCluster? cluster = null;
        var lifetime = new TestSessionLifetime(options.Execution);
        try
        {
            cluster = builder.Build();
            lifetime.Own("cluster", cluster);
            await cluster.DeployAsync(deadline.Token).ConfigureAwait(false);
            var brain = new ModuleBrain(cluster, options.Execution, lifetime);
            if (options.HttpEdge)
            {
                var edge = await ModuleHttpEdge.StartAsync(brain, modules, options, masterKey, siloServiceTypes, deadline.Token).ConfigureAwait(false);
                lifetime.Own("http-edge", edge);
                brain.AttachHttpEdge(edge);
            }
            return brain;
        }
        catch (Exception error)
        {
            try { await lifetime.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { error.Data["StartupRollbackFailure"] = cleanup; }
            throw;
        }
    }
}
