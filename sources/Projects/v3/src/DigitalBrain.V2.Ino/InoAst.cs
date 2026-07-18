namespace DigitalBrain.V2.Ino;

public sealed record InoProgram(
    string NeuronFqn,
    InoSynapsePort[] Synapses,
    InoNeuronPort[] Neurons,
    string[] Broadcasts,
    string[] Handles,
    InoState[] States,
    InoHandler[] Handlers,
    InoScenario[] Scenarios)
{
    public InoSynapsePort Synapse(string alias) =>
        Synapses.FirstOrDefault(port => string.Equals(port.Alias, alias, StringComparison.Ordinal))
        ?? throw new InoException($"Unknown synapse alias '{alias}'.");

    public InoNeuronPort Neuron(string alias) =>
        Neurons.FirstOrDefault(port => string.Equals(port.Alias, alias, StringComparison.Ordinal))
        ?? throw new InoException($"Unknown neuron alias '{alias}'.");
}

public sealed record InoSynapsePort(string Alias, string Fqn);

public sealed record InoNeuronPort(string Alias, string Fqn);

public sealed record InoState(string Name, string Type);

public sealed record InoHandler(string SynapseAlias, InoStatement[] Body);

public abstract record InoStatement;

public sealed record SetStateStatement(string Name, InoExpression Value) : InoStatement;

public sealed record EmitStatement(string SynapseAlias, InoArgument[] Arguments) : InoStatement;

public sealed record AskStatement(string TargetAlias, string SynapseAlias, InoArgument[] Arguments) : InoStatement;

public sealed record ReplyStatement(string SynapseAlias, InoArgument[] Arguments) : InoStatement;

public sealed record InoArgument(string Name, InoExpression Value);

public abstract record InoExpression;

public sealed record StringLiteralExpression(string Value) : InoExpression;

public sealed record RawLiteralExpression(string Value) : InoExpression;

public sealed record FieldExpression(string Alias, string Field) : InoExpression;

public sealed record InoScenario(string Description, InoScenarioStep[] Steps);

public sealed record InoScenarioStep(string Keyword, string Text);

public sealed class InoException(string message) : InvalidOperationException(message);
