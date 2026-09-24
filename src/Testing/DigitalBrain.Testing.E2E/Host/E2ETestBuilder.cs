using DigitalBrain.Core;

namespace DigitalBrain.Testing.E2E;

public sealed class E2ETestBuilder
{
    private readonly BrainCompositionBuilder _composition = new();
    private TestExecutionOptions _execution = new();
    private BrowserOptions _browser = new() { Headless = true };
    private string? _durableStorageKey;
    private bool _started;

    public E2ETestBuilder WithModule<TModule>(Action<ModuleConfiguration<TModule>>? configure = null)
        where TModule : class, IModule, new()
    {
        EnsureMutable();
        using var scope = BrowserConfiguration.Begin(_browser);
        _composition.WithModule(configure);
        _browser = scope.Options;
        return this;
    }

    public E2ETestBuilder ConfigureModule<TModule>(Action<ModuleConfiguration<TModule>> configure)
        where TModule : class, IModule, new()
    {
        EnsureMutable();
        using var scope = BrowserConfiguration.Begin(_browser);
        _composition.ConfigureModule(configure);
        _browser = scope.Options;
        return this;
    }

    public E2ETestBuilder WithExecution(TestExecutionOptions execution)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(execution);
        _execution = execution;
        return this;
    }

    public E2ETestBuilder WithBrowser(BrowserOptions browser)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(browser);
        _browser = browser;
        return this;
    }

    // Keeps the cluster storage data volume across host restarts within one test, so a host that
    // is killed and started again reads the persisted grain state. Every host of the same test
    // passes the same key; the volume is removed when the test process exits.
    public E2ETestBuilder WithDurableStorage(string key)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _durableStorageKey = DurableStorageVolume.Acquire(key);
        return this;
    }

    internal BrainComposition BuildComposition()
    {
        var composition = _composition.Build();
        if (composition.RequiresLocalServices)
        { throw new NotSupportedException("Hosted tests run in another process. Select a compiled provider or a hosted endpoint fixture."); }
        return composition;
    }

    public Task<E2EBrain> StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureMutable();
        var composition = BuildComposition();
        _started = true;
        return E2ETest.StartModulesAsync(composition.Modules, _execution, _browser, _durableStorageKey, cancellationToken);
    }

    private void EnsureMutable()
    {
        if (_started) { throw new InvalidOperationException("A test builder starts one session. Create another builder for a new session."); }
    }
}
