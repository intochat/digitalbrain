namespace DigitalBrain.Core.Neurons;

public interface IEntity<T>
{
    Task SaveAsync(T data, CancellationToken cancellationToken);
    Task<T> ReadAsync(CancellationToken cancellationToken);
}
