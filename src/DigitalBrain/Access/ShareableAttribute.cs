namespace DigitalBrain.Access;

/// <summary>Marks a neuron contract interface as eligible for cross-brain sharing and capability grants.</summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
public sealed class ShareableAttribute : Attribute;
