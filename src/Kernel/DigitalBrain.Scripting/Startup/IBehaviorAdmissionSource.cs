namespace DigitalBrain.Scripting.Startup;

internal interface IBehaviorAdmissionSource
{
    IAsyncEnumerable<AdmittedBehavior> WatchAsync(CancellationToken cancellationToken);
}
