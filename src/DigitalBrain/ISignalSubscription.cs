namespace DigitalBrain.Contracts;

public interface ISignalSubscription<T> : IAsyncDisposable where T : Signal
{
    Task Completion { get; }
    IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default);
}
