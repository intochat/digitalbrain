namespace DigitalBrain.Contracts;

public interface IDigitalBrain : IAsyncDisposable
{
    T Get<T>(string id) where T : class, IGrainWithStringKey;
    Task<ISignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal;
}