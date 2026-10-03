namespace DigitalBrain.Aspire.Hosting;

public sealed class DigitalBrainClientReference
{
    internal DigitalBrainClientReference(DigitalBrainBuilder brain, bool shareHttpIdentity = false)
    { Brain = brain; ShareHttpIdentity = shareHttpIdentity; }

    internal bool ShareHttpIdentity { get; }

    internal DigitalBrainBuilder Brain { get; }
}
