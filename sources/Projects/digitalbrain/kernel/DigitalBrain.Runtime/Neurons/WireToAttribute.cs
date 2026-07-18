namespace DigitalBrain.Runtime.Neurons;

using System;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class WireToAttribute : Attribute
{
    public string Target { get; }

    public WireToAttribute(string target)
    {
        Target = target;
    }
}
