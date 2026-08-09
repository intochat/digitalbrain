namespace DigitalBrain.Aspire.Hosting;

public sealed class ClientDigitalBrainReference
{
    internal ClientDigitalBrainReference(DigitalBrainBuilder brain) => Brain = brain;

    internal DigitalBrainBuilder Brain { get; }
}
