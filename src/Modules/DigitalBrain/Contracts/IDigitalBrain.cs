namespace DigitalBrain.Contracts;

public interface IDigitalBrain : IAsyncDisposable
{
    T Get<T>(string id) where T : class, IGrainWithStringKey;
    Task<IReadOnlyList<Signal>> Signals();
    IAsyncEnumerable<T> On<T>(INeuron neuron, CancellationToken cancellation = default) where T : Signal;
    HttpClient Http { get; }
}
