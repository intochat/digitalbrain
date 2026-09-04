using DigitalBrain.Abstractions;

namespace DigitalBrain.Scripting.Startup;

public sealed class StartupScriptContext
{
    internal StartupScriptContext(IDigitalBrain brain, CancellationToken cancellationToken)
    {
        Brain = brain;
        CancellationToken = cancellationToken;
    }

    public IDigitalBrain Brain { get; }

    public CancellationToken CancellationToken { get; }
}
