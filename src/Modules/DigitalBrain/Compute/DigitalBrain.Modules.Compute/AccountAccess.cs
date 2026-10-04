using DigitalBrain.Kernel.Enforcement;

namespace DigitalBrain.Compute;

// Compute neurons belong to an account, not a brain: any of the account's own brains reads
// and spends them, nobody else's does. That rule is expressed through the scope the kernel
// already matches - the caller's own scope exactly when the account is theirs, unclassified
// (and therefore refused for stamped callers) otherwise.
internal static class AccountAccess
{
    public static NeuronAccess For(string accountKey)
        => CallerContextStamper.TryGet(out var caller) && caller.AccountId == accountKey
            ? new(BrainScope.Create(caller.AccountId, caller.BrainId).Id)
            : NeuronAccess.Unclassified;
}
