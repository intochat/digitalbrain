using DigitalBrain.Client;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;

namespace DigitalBrain.Testing.Unit;

public sealed class UnitTestBuilder
{
    private readonly BrainCompositionBuilder _composition = new();
    private UnitOptions _options = new();
    private bool _started;

    public UnitTestBuilder WithModule<TModule>(Action<ModuleConfiguration<TModule>>? configure = null)
        where TModule : class, IModule, new()
    {
        EnsureMutable();
        _composition.WithModule(configure);
        return this;
    }
    public UnitTestBuilder RequireModules(IEnumerable<Type> modules)
    {
        EnsureMutable();
        _composition.RequireModules(modules);
        return this;
    }
    public UnitTestBuilder ConfigureModule<TModule>(Action<ModuleConfiguration<TModule>> configure)
        where TModule : class, IModule, new()
    {
        EnsureMutable();
        _composition.ConfigureModule(configure);
        return this;
    }
    public UnitTestBuilder WithExecution(TestExecutionOptions execution)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(execution);
        _options = _options with { Execution = execution };
        return this;
    }
    public UnitTestBuilder ConfigureSilo(Action<ISiloBuilder> configure)
    {
        EnsureMutable();
        _options = _options with { ConfigureSilo = _options.ConfigureSilo + configure };
        return this;
    }
    public UnitTestBuilder ConfigureClient(Action<IClientBuilder> configure)
    {
        EnsureMutable();
        _options = _options with { ConfigureClient = _options.ConfigureClient + configure };
        return this;
    }
    // Short leases and renewals so subscription expiry and reactivation show up within a test's timeout.
    public UnitTestBuilder WithFastSubscriptions(int bufferCapacity = 256)
    {
        static void Shorten(BrainOptions options)
        {
            options.ObserverLease = TimeSpan.FromSeconds(2);
            options.RenewEvery = TimeSpan.FromMilliseconds(200);
            options.OperationTimeout = TimeSpan.FromMilliseconds(500);
        }
        return ConfigureSilo(silo => silo.Services.Configure<BrainOptions>(Shorten))
            .ConfigureClient(client => client.Services.Configure<BrainOptions>(options => { Shorten(options); options.BufferCapacity = bufferCapacity; }));
    }
    public UnitTestBuilder WithReminders()
    {
        EnsureMutable();
        _options = _options with { UseReminders = true };
        return this;
    }
    public Task<UnitBrain> StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureMutable();
        var composition = _composition.Build();
        _started = true;
        return UnitTest.StartAsync(_options with
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
