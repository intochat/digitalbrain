namespace DigitalBrain.Execution;

public sealed class ExecutionModule : Core.IModule
{
    public void Configure(ISiloBuilder builder)
        => ArgumentNullException.ThrowIfNull(builder);
}
