namespace DigitalBrain;

public readonly record struct Dispatch
{
    private Dispatch(NeuronId? receiver) => Receiver = receiver;

    public static Dispatch Broadcast { get; } = new(receiver: null);

    public NeuronId? Receiver { get; }

    public bool IsDirect => Receiver.HasValue;

    public static Dispatch Direct(NeuronId receiver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiver.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(receiver.Name);
        return new Dispatch(receiver);
    }
}
