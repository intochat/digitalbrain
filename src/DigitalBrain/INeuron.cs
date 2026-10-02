namespace DigitalBrain;

public interface INeuron
{
    IAsyncEnumerable<T> Watch<T>(CancellationToken cancellationToken = default) where T : Signal;
}
