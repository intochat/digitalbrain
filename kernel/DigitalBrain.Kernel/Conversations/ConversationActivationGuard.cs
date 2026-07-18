namespace DigitalBrain.Kernel;

internal sealed class ConversationActivationGuard
{
    private bool _valid = true;

    public void EnsureValid()
    {
        if (!_valid)
            throw new BrainException(
                NeuronFailureKind.StorageUnavailable,
                "The conversation activation is awaiting durable-state recovery.");
    }

    public void Invalidate() => _valid = false;
}
