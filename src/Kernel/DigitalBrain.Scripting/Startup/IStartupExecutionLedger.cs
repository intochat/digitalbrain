namespace DigitalBrain.Scripting.Startup;

internal interface IStartupExecutionLedger
{
    Task<StartupExecution?> FindAsync(StartupExecutionKey key, CancellationToken cancellationToken);

    Task RecordAsync(StartupExecution execution, CancellationToken cancellationToken);
}
