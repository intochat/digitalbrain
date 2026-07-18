namespace Ino.NeuronTesting.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NeuronIdAttribute : Attribute
{
    public NeuronIdAttribute(string value) => Value = value;
    public string Value { get; }
}
