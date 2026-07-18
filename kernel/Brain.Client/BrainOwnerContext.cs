using Brain.Contracts;

namespace Brain.Client;

public sealed class BrainOwnerContext
{
    private readonly AsyncLocal<BrainOwnerId?> _currentOwner = new();

    public BrainOwnerId? Current
    {
        get => _currentOwner.Value;
        set => _currentOwner.Value = value;
    }
}
