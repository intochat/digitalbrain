using DigitalBrain.Core;

namespace DigitalBrain.Testing.E2E;

public sealed class E2ETestBuilder<TAppHost> where TAppHost : class
{
    private readonly CompositionOverrides _overrides = new();
    private TestExecutionOptions _execution = new();
    private BrowserOptions _browser = new() { Headless = true };
    private bool _started;

    public E2ETestBuilder<TAppHost> ConfigureModule<TModule, TOptions>(Action<TOptions> configureOptions)
        where TModule : class, IModule<TOptions>, new() where TOptions : class, IModuleOptions, new()
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configureOptions);
        var browserAtDeclaration = _browser;
        using var scope = BrowserConfiguration.Begin(_browser);
        // The edit runs once here so ambient browser choices apply and a throwing edit fails at the call site;
        // it runs again later against the AppHost's own options, where browser choices are already taken.
        configureOptions(new TOptions());
        _browser = scope.Options;
        _overrides.ConfigureModule<TModule, TOptions>(options =>
        {
            using var discarded = BrowserConfiguration.Begin(browserAtDeclaration);
            configureOptions(options);
        });
        return this;
    }

    public E2ETestBuilder<TAppHost> ConfigureModule<TModule>(Action<ModuleConfiguration<TModule>> configure)
        where TModule : class, IModule, new()
    {
        EnsureMutable();
        using var scope = BrowserConfiguration.Begin(_browser);
        _overrides.ConfigureModule(configure);
        _browser = scope.Options;
        return this;
    }
    public E2ETestBuilder<TAppHost> WithExecution(TestExecutionOptions execution)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(execution);
        _execution = execution;
        return this;
    }

    /// <summary>
    /// Adds environment variables to the primary application resource, for test-only wiring such
    /// as pointing the OTLP exporter at a collector owned by the test process.
    /// </summary>
    public E2ETestBuilder<TAppHost> WithResourceEnvironment(IReadOnlyDictionary<string, string> environment)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(environment);
        _execution = _execution with { ResourceEnvironment = environment };
        return this;
    }
    public E2ETestBuilder<TAppHost> WithBrowser(BrowserOptions browser)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(browser);
        _browser = browser;
        return this;
    }

    public Task<E2EBrain> StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureMutable();
        var overrides = SerializeOverrides();
        _started = true;
        return E2ETest.StartAsync<TAppHost>(overrides, _execution, _browser, cancellationToken);
    }
    internal string SerializeOverrides() => _overrides.Serialize();
    internal BrowserOptions BrowserOptions => _browser;
    private void EnsureMutable()
    {
        if (_started) { throw new InvalidOperationException("A test builder starts one session. Create another builder for a new session."); }
    }
}