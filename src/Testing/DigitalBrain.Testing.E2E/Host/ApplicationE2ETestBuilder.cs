using DigitalBrain.Core;

namespace DigitalBrain.Testing.E2E;

public sealed class E2ETestBuilder<TAppHost> where TAppHost : class
{
    private readonly CompositionOverrides _overrides = new();
    private TestExecutionOptions _execution = new();
    private BrowserOptions _browser = new() { Headless = true };
    private string? _executionRootKey;
    private TestExecutionRoot? _executionRoot;
    private bool _started;

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

    /// <summary>
    /// Gives the AppHost a unique per-run root for a configuration key (for example the behavior
    /// authoring root), so a test never shares the developer's persisted execution directories.
    /// </summary>
    public E2ETestBuilder<TAppHost> WithExecutionRoot(string configurationKey)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationKey);
        _executionRootKey = configurationKey;
        return this;
    }

    public Task<E2EBrain> StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureMutable();
        var overrides = SerializeOverrides();
        var executionRoot = AcquireExecutionRoot();
        _started = true;
        return E2ETest.StartAsync<TAppHost>(overrides, _execution, _browser, executionRoot, cancellationToken);
    }
    internal string SerializeOverrides() => _overrides.Serialize();
    internal BrowserOptions BrowserOptions => _browser;
    internal TestExecutionRoot? ExecutionRoot => _executionRoot;

    internal IReadOnlyList<string> HostArguments(string identity)
    {
        EnsureMutable();
        return E2ETest.ComposeHostArguments(SerializeOverrides(), identity, AcquireExecutionRoot());
    }

    private TestExecutionRoot? AcquireExecutionRoot()
        => _executionRootKey is null ? null : _executionRoot ??= TestExecutionRoot.Create(_executionRootKey);

    private void EnsureMutable()
    {
        if (_started) { throw new InvalidOperationException("A test builder starts one session. Create another builder for a new session."); }
    }
}