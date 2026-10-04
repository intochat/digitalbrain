using DigitalBrain.Client;
using DigitalBrain.Client.Orleans;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;

namespace DigitalBrain.Testing.Module;

public sealed class ModuleTestBuilder
{
    private readonly BrainCompositionBuilder _composition = new();
    private ModuleOptions _options = new();
    private bool _started;

    public ModuleTestBuilder WithModule<TModule>(Action<ModuleConfiguration<TModule>>? configure = null)
        where TModule : class, IModule, new()
    {
        EnsureMutable();
        _composition.WithModule(configure);
        return this;
    }
    public ModuleTestBuilder WithModule<TModule, TOptions>(Action<TOptions>? configureOptions = null,
        Action<ModuleConfiguration<TModule>>? configure = null)
        where TModule : class, IModule<TOptions>, new() where TOptions : class, IModuleOptions, new()
    {
        EnsureMutable();
        _composition.WithModule(configureOptions, configure);
        return this;
    }
    public ModuleTestBuilder RequireModules(IEnumerable<Type> modules)
    {
        EnsureMutable();
        _composition.RequireModules(modules);
        return this;
    }
    public ModuleTestBuilder ConfigureModule<TModule>(Action<ModuleConfiguration<TModule>> configure)
        where TModule : class, IModule, new()
    {
        EnsureMutable();
        _composition.ConfigureModule(configure);
        return this;
    }
    public ModuleTestBuilder ConfigureModule<TModule, TOptions>(Action<TOptions> configureOptions)
        where TModule : class, IModule<TOptions>, new() where TOptions : class, IModuleOptions, new()
    {
        EnsureMutable();
        _composition.ConfigureModule<TModule, TOptions>(configureOptions);
        return this;
    }
    public ModuleTestBuilder WithExecution(TestExecutionOptions execution)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(execution);
        _options = _options with { Execution = execution };
        return this;
    }
    public ModuleTestBuilder ConfigureSilo(Action<ISiloBuilder> configure)
    {
        EnsureMutable();
        _options = _options with { ConfigureSilo = _options.ConfigureSilo + configure };
        return this;
    }
    public ModuleTestBuilder ConfigureClient(Action<IClientBuilder> configure)
    {
        EnsureMutable();
        _options = _options with { ConfigureClient = _options.ConfigureClient + configure };
        return this;
    }
    // Short leases and renewals so subscription expiry and reactivation show up within a test's timeout.
    public ModuleTestBuilder WithFastSubscriptions(int bufferCapacity = 256)
    {
        static void Shorten(SubscriptionOptions options)
        {
            options.RenewEvery = TimeSpan.FromMilliseconds(200);
            options.OperationTimeout = TimeSpan.FromMilliseconds(500);
        }
        return ConfigureSilo(silo => silo.Services.Configure<ObserverOptions>(options => options.Lease = TimeSpan.FromSeconds(2)))
            .ConfigureClient(client => client.Services.Configure<SubscriptionOptions>(options => { Shorten(options); options.BufferCapacity = bufferCapacity; }));
    }
    // Hosts the composed modules' HTTP endpoints through the product pipeline on a loopback
    // port; the brain then answers HTTP through ModuleBrain.HttpClient.
    public ModuleTestBuilder WithHttpEdge()
    {
        EnsureMutable();
        _options = _options with { HttpEdge = true };
        return this;
    }
    public ModuleTestBuilder WithReminders()
    {
        EnsureMutable();
        _options = _options with { UseReminders = true };
        return this;
    }
    public Task<ModuleBrain> StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureMutable();
        var composition = _composition.Build();
        _started = true;
        return ModuleTest.StartAsync(_options with
        {
            Modules = composition.Modules,
            ConfigureSilo = silo => { composition.ConfigureLocalServices(silo.Services); _options.ConfigureSilo?.Invoke(silo); },
        }, cancellationToken);
    }
    private void EnsureMutable()
    {
        if (_started) { throw new InvalidOperationException("A test builder starts one session. Create another builder for a new session."); }
    }
}
