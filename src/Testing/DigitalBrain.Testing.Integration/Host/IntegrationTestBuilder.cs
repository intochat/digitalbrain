using DigitalBrain.Core;

namespace DigitalBrain.Testing.Integration;

public sealed class IntegrationTestBuilder
{
    private readonly BrainCompositionBuilder _composition = new();
    private TestExecutionOptions _execution = new();
    private bool _started;
    public IntegrationTestBuilder WithModule<TModule>(Action<ModuleConfiguration<TModule>>? configure = null)
        where TModule : class, IModule, new()
    {
        EnsureMutable();
        _composition.WithModule(configure);
        return this;
    }
    public IntegrationTestBuilder ConfigureModule<TModule>(Action<ModuleConfiguration<TModule>> configure)
        where TModule : class, IModule, new()
    {
        EnsureMutable();
        _composition.ConfigureModule(configure);
        return this;
    }
    public IntegrationTestBuilder WithExecution(TestExecutionOptions execution)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(execution);
        _execution = execution;
        return this;
    }
    public Task<IntegrationBrain> StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureMutable();
        var composition = _composition.Build();
        if (composition.RequiresLocalServices)
            { throw new NotSupportedException("Integration tests run in another process. Select a compiled provider or a hosted endpoint fixture."); }
        _started = true;
        return IntegrationTest.StartAsync(composition.Modules, _execution, cancellationToken);
    }
    private void EnsureMutable()
    {
        if (_started) { throw new InvalidOperationException("A test builder starts one session. Create another builder for a new session."); }
    }
}
