namespace DigitalBrain.Contracts;

public interface IDigitalBrain : IAsyncDisposable
{
    T Get<T>(string id) where T : class, IGrainWithStringKey;
}
