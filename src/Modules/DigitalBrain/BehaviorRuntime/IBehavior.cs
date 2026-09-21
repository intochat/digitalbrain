namespace DigitalBrain.Core;

public interface IBehavior
{
    Task RunAsync(CancellationToken cancellation = default);
}