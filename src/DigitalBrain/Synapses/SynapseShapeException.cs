namespace DigitalBrain.Units;

/// <summary>A method marked <c>[Synapse]</c> or a <c>[State]</c> record does not have a shape the runtime can compile.</summary>
/// <param name="message">The reason, naming the function or record.</param>
public sealed class SynapseShapeException(string message) : Exception(message);
