namespace DigitalBrain.Kernel.Foundry;

public interface IResourceController
{
    Task RestartKernelAsync(string reason, CancellationToken cancellationToken = default);
}

public sealed class AspireResourceController(ILogger<AspireResourceController> logger) : IResourceController
{
    private readonly ILogger<AspireResourceController> _logger = logger;

    public Task RestartKernelAsync(string reason, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The actual restart is performed out-of-band by the Aspire MCP tool
        // execute_resource_command("restart","kernel"). This controller records intent;
        // the orchestrator emits KernelRestartRequested which the MCP-driven loop consumes.
        _logger.LogWarning("Kernel restart requested: {Reason}. Trigger via Aspire MCP execute_resource_command(restart, kernel).", reason);
        return Task.CompletedTask;
    }
}
