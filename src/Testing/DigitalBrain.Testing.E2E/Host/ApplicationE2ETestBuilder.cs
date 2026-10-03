using DigitalBrain.Contracts;
using DigitalBrain.Kernel;

namespace DigitalBrain.Testing.E2E;

public sealed class E2ETestBuilder<TAppHost> where TAppHost : class
{
    // A configured module replaces the AppHost's code-declared options with fixture options.
    // Repeated edits compose on the same options, including empty collections and false values.
    private readonly Dictionary<Type, object> _moduleOptions = [];
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
        var edited = _moduleOptions.TryGetValue(typeof(TModule), out var existing) ? (TOptions)existing : new TOptions();
        configureOptions(edited);
        _browser = scope.Options;
        edited.Validate();
        _moduleOptions[typeof(TModule)] = edited;
        var id = ModuleIdentity.Get(typeof(TModule));
        var prefix = $"DigitalBrain:Modules:{id}:";
        foreach (var key in _optionOverrides.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
        { _optionOverrides.Remove(key); }
        _optionOverrides[prefix + "ReplaceOptions"] = "true";
        foreach (var (key, value) in ModuleOptionsSerialization.FlattenOptions(edited, id))
        { if (value is not null) { _optionOverrides[key] = value; } }
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
