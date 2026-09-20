namespace DigitalBrain.Testing;

public interface ITrackedBrain
{
    int BufferCapacity { get; }
    void Track(IAsyncDisposable resource);
}
