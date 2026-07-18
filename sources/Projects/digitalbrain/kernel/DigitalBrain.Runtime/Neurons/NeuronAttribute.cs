namespace DigitalBrain.Runtime.Neurons;

using System;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class NeuronAttribute : Attribute
{
    public string? Name { get; }

    public NeuronAttribute()
    {
    }

    public NeuronAttribute(string name)
    {
        Name = name;
    }
}
