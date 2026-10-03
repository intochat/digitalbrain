using DigitalBrain.Kernel;

namespace DigitalBrain.Testing.E2E;

public sealed class E2ETestBuilder<TAppHost> where TAppHost : class
{
    // Option edits become plain configuration keys passed to the AppHost as arguments; the
    // AppHost's configuration overrides its code-declared defaults by standard precedence.
    private readonly Dictionary<string, string?> _optionOverrides = new(StringComparer.OrdinalIgnoreCase);
    private TestExecutionOptions _execution = new();
    private BrowserOptions _browser = new() { Headless = true };
    private bool _started;

    public E2ETestBuilder<TAppHost> ConfigureModule<TModule, TOptions>(Action<TOptions> configureOptions)
        where TModule : class, IModule<TOptions>, new() where TOptions : class, IModuleOptions, new()
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(configureOptions);
        using var scope = BrowserConfiguration.Begin(_browser);
        var edited = new TOptions();
        configureOptions(edited);
        _browser = scope.Options;
        // Only the keys the edit changed override the AppHost's own values. An edit that clears
        // a default collection entry cannot be expressed as configuration and keeps the default.
        var defaults = ModuleOptionsSerialization.FlattenOptions(new TOptions(), typeof(TModule).Name);
        foreach (var (key, value) in ModuleOptionsSerialization.FlattenOptions(edited, typeof(TModule).Name))
        {
            if (!defaults.TryGetValue(key, out var baseline) || baseline != value) { _optionOverrides[key] = value; }
        }
        return this;
    }

    // A startup budget for hosts whose resources compile at start (a CI runner building the
    // web shell needs more than the default); merges into the composition's execution options.
    public E2ETestBuilder<TAppHost> WithStartupTimeout(TimeSpan timeout)
    {
        EnsureMutable();
        TestExecutionOptions.ValidateTimeout(timeout);
        _execution = _execution with { StartupTimeout = timeout };
        return this;
    }

    public E2ETestBuilder<TAppHost> WithExecution(TestExecutionOptions execution)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(execution);
        _execution = execution;
        return this;
    }

    // Adds environment variables to the primary application resource, for test-only wiring such as
    // pointing the OTLP exporter at a collector owned by the test process. Later calls merge over
    // earlier ones, so a composition's defaults survive a fact adding its own variables.
    public E2ETestBuilder<TAppHost> WithResourceEnvironment(IReadOnlyDictionary<string, string> environment)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(environment);
        var merged = new Dictionary<string, string>(_execution.ResourceEnvironment, StringComparer.Ordinal);
        foreach (var (key, value) in environment) { merged[key] = value; }
        _execution = _execution with { ResourceEnvironment = merged };
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
        _started = true;
        return E2ETest.StartAsync<TAppHost>(_optionOverrides, _execution, _browser, cancellationToken);
    }

    internal IReadOnlyDictionary<string, string?> OptionOverrides => _optionOverrides;
    internal BrowserOptions BrowserOptions => _browser;

    private void EnsureMutable()
    {
        if (_started) { throw new InvalidOperationException("A test builder starts one session. Create another builder for a new session."); }
    }
}
