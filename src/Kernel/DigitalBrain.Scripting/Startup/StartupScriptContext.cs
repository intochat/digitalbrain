using DigitalBrain.Abstractions;

namespace DigitalBrain.Scripting.Startup;

public sealed class StartupScriptContext
{
    internal StartupScriptContext(IDigitalBrain brain, CancellationToken cancellationToken, ScriptBehavior? behavior = null)
    {
        Brain = brain;
        CancellationToken = cancellationToken;
        Behavior = behavior;
    }

    public IDigitalBrain Brain { get; }

    public CancellationToken CancellationToken { get; }

    public ScriptBehavior? Behavior { get; }
}
