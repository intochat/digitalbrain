using DigitalBrain.Abstractions;

namespace DigitalBrain.Scripting.Startup;

public sealed class StartupScriptContext(IDigitalBrain brain, CancellationToken cancellationToken)
{
    public IDigitalBrain Brain { get; } = brain;

    public CancellationToken CancellationToken { get; } = cancellationToken;
}
