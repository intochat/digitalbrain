using DigitalBrain.Core;

namespace DigitalBrain.Testing.E2E;

public sealed class E2ETestBuilder<TAppHost> where TAppHost : class
{
    private readonly CompositionOverrides _overrides = new();
    private TestExecutionOptions _execution = new();
    private BrowserOptions _browser = new();
    private bool _started;

    public E2ETestBuilder<TAppHost> ConfigureModule<TModule>(Action<ModuleConfiguration<TModule>> configure)
        where TModule : class, IModule, new()
    {
        EnsureMutable();
        _overrides.ConfigureModule(configure);
        return this;
    }
    public E2ETestBuilder<TAppHost> WithExecution(TestExecutionOptions execution)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(execution);
        _execution = execution;
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
        var overrides = _overrides.Serialize();
        _started = true;
        return E2ETest.StartAsync<TAppHost>(overrides, _execution, _browser, cancellationToken);
    }
    private void EnsureMutable()
    {
        if (_started) { throw new InvalidOperationException("A test builder starts one session. Create another builder for a new session."); }
    }
}
