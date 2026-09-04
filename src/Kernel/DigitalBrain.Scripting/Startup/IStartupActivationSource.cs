namespace DigitalBrain.Scripting.Startup;

internal interface IStartupActivationSource
{
    IAsyncEnumerable<StartupActivation> WatchAsync(CancellationToken cancellationToken);
}
