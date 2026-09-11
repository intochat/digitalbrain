using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Core;

internal interface ICommandHost
{
    NeuronId Id { get; }
    bool HasPendingRoom { get; }
    ReactionContext? ReactionContext { get; set; }
    Task PersistAsync();
    Task DiscardStagedChangesAsync(Exception cause);
    void AdmitTurnWork();
    Task WakeTurnWorkAsync();
}
