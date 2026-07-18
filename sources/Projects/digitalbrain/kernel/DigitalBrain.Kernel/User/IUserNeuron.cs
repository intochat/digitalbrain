using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Kernel.User;

public interface IUserNeuron : INeuronWithStringKey
{
    Task SubmitPromptAsync(string text, Guid correlationId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetRecentCorrelationIdsAsync(TimeSpan since, CancellationToken ct);
}
