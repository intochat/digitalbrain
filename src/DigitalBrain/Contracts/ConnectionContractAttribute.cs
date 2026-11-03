namespace DigitalBrain.Contracts;

/// <summary>Marks a plain contract every connection neuron may implement: the connection key names its neuron type.</summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class ConnectionContractAttribute : Attribute;
