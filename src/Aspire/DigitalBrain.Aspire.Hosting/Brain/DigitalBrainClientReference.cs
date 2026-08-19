namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainClientReference
{
    internal DigitalBrainClientReference(DigitalBrainBuilder brain) => Brain = brain;

    internal DigitalBrainBuilder Brain { get; }
}
