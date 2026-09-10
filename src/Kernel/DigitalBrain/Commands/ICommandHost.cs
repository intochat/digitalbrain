using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Core;

internal interface ICommandHost
{
    NeuronId Id { get; }
    ReactionContext? ReactionContext { get; set; }
    Task PersistAsync();
    Task DiscardStagedChangesAsync(Exception cause);
    void AdmitCommandWork();
    Task WakeCommandWorkAsync();
}
