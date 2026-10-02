namespace DigitalBrain;

public interface INeuron
{
    IAsyncEnumerable<T> Watch<T>(CancellationToken cancellationToken) where T : Signal;
}
