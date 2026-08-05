namespace DigitalBrain;

public interface INeuron<in TSynapse>
    where TSynapse : Synapse
{
    Task HandleAsync(TSynapse synapse, CancellationToken cancellationToken);
}
