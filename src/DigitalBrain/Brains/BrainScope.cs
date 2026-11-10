using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Brains;

public sealed record BrainScope(string Id, string Owner)
{
    public static string CurrentId()
    {
        if (!CallerAdmission.TryGetCaller(out var caller))
        {
            throw new InvalidOperationException("No caller context is stamped on this call.");
        }

        return Create(caller.AccountId, caller.BrainId).Id;
    }

    public static BrainScope Create(string owner, string name)
    {
        if (!BrainId.IsValid(name))
        {
            throw new ArgumentException("A brain ID must contain 1–200 characters without path separators or ':'.", nameof(name));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        return new(Id: name, Owner: owner);
    }

    public static bool IsValidId([NotNullWhen(true)] string? value) => BrainId.IsValid(value);
}
