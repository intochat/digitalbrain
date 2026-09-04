using DigitalBrain.Abstractions;
using Microsoft.Extensions.Hosting;

namespace DigitalBrainConsole;

// Host-side boot, same idea as WinUI IActivationHandler: ordered steps that bring the
// process up. Cluster-side boot is DigitalBrainActivated handled by neurons (IAspire,
// IHealth, IConsole). Do not duplicate this as a second in-silo framework.
internal interface IActivationHandler
{
    Task HandleAsync(ActivationContext context, CancellationToken cancellationToken);
}

internal sealed class ActivationContext(string[] args)
{
    public string[] Args { get; } = args;
    public IHost? Host { get; set; }
    public IDigitalBrain? Brain { get; set; }
}

internal sealed class ActivationService(IReadOnlyList<IActivationHandler> handlers)
{
    public static ActivationService Default { get; } = new(
    [
        new StartAspireHandler(),
        new ConnectSiloHandler(),
        new ActivateBrainHandler(),
        new VerifyHealthHandler(),
    ]);

    public async Task ActivateAsync(ActivationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handler.HandleAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed class StartAspireHandler : IActivationHandler
{
    public Task HandleAsync(ActivationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return AspireApp.StartDistributedAppAsync(appHostProject: null, cancellationToken);
    }
}

internal sealed class ConnectSiloHandler : IActivationHandler
{
    public async Task HandleAsync(ActivationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var hosted = await Brain.StartLocalSiloAsync(context.Args, cancellationToken)
            .ConfigureAwait(false);
        context.Host = hosted.Host;
        context.Brain = hosted;
    }
}

internal sealed class ActivateBrainHandler : IActivationHandler
{
    public Task HandleAsync(ActivationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Brain);
        return context.Brain.ActivateAsync(cancellationToken);
    }
}

internal sealed class VerifyHealthHandler : IActivationHandler
{
    public async Task HandleAsync(ActivationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Brain);

        var healthy = await context.Brain.GetGrainProxy<IHealth>().Verify(cancellationToken)
            .ConfigureAwait(false);
        if (!healthy)
        {
            throw new InvalidOperationException("DigitalBrain health check failed after activation.");
        }
    }
}
