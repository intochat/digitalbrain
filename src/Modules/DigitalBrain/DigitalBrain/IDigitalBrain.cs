using DigitalBrain.Contracts;
namespace DigitalBrain.Core;
public interface IDigitalBrain
{
    T Get<T>(string id) where T : class, IGrainWithStringKey;
    Task<SignalSubscription<T>> SubscribeAsync<T>(INeuron source, CancellationToken cancellationToken = default) where T : Signal;
}
