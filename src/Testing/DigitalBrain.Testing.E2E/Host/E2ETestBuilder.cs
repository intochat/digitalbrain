using DigitalBrain.Contracts;
using DigitalBrain.Kernel;

namespace DigitalBrain.Testing.E2E;

public sealed class E2ETestBuilder
{
    private readonly BrainCompositionBuilder _composition = new();
    private TestExecutionOptions _execution = new();
    private BrowserOptions _browser = new() { Headless = true };
    private DurableStorageVolume? _durableStorage;
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

    public E2ETestBuilder WithModule<TModule, TOptions>(Action<TOptions>? configureOptions = null,
        Action<ModuleConfiguration<TModule>>? configure = null)
        where TModule : class, IModule<TOptions>, new() where TOptions : class, IModuleOptions, new()
    {
        EnsureMutable();
        using var scope = BrowserConfiguration.Begin(_browser);
        _composition.WithModule(configureOptions, configure);
        _browser = scope.Options;
        return this;
    }

    public E2ETestBuilder ConfigureModule<TModule, TOptions>(Action<TOptions> configureOptions)
        where TModule : class, IModule<TOptions>, new() where TOptions : class, IModuleOptions, new()
    {
        EnsureMutable();
        using var scope = BrowserConfiguration.Begin(_browser);
        _composition.ConfigureModule<TModule, TOptions>(configureOptions);
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

    public E2ETestBuilder WithDurableStorage(DurableStorageVolume volume)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(volume);
        _durableStorage = volume;
        return this;
    }

    public E2ETestBuilder WithStartupTimeout(TimeSpan timeout)
    {
        EnsureMutable();
        TestExecutionOptions.ValidateTimeout(timeout);
        _execution = _execution with { StartupTimeout = timeout };
        return this;
    }


    public E2ETestBuilder WithResourceEnvironment(IReadOnlyDictionary<string, string> environment)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(environment);
        var merged = new Dictionary<string, string>(_execution.ResourceEnvironment, StringComparer.Ordinal);
        foreach (var (key, value) in environment) { merged[key] = value; }
        _execution = _execution with { ResourceEnvironment = merged };
        return this;
    }

    public BrainComposition BuildComposition()
    {
        var composition = _composition.Build();
        if (composition.RequiresLocalServices)
        { throw new NotSupportedException("Hosted tests run in another process. Select a compiled provider or a hosted endpoint fixture."); }
        return composition;
    }

    public async Task<E2EBrain> StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureMutable();
        var composition = BuildComposition();
        _started = true;
        if (_durableStorage is not null) { await _durableStorage.WaitUntilReleasedAsync(cancellationToken); }
        return await E2ETest.StartModulesAsync(composition.Modules, _execution, _browser, _durableStorage?.Name, cancellationToken);
    }

    private void EnsureMutable()
    {
        if (_started) { throw new InvalidOperationException("A test builder starts one session. Create another builder for a new session."); }
    }
}
