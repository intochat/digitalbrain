using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

public sealed class CapabilityTool
{
    private readonly AIFunction _function;

    public CapabilityTool(string name, string description, Delegate invoke)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(invoke);

        _function = AIFunctionFactory.Create(invoke, name, description);
    }

    public string Name => _function.Name;

    internal AIFunction BindTo(TaskScheduler turnScheduler)
        => new TurnBoundFunction(_function, turnScheduler);
}
