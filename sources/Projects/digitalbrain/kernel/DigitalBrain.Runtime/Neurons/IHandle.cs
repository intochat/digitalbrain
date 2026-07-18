namespace DigitalBrain.Runtime.Neurons;

public interface IHandle<in TSynapse> where TSynapse : Synapse
{
    Task HandleAsync(TSynapse synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}
