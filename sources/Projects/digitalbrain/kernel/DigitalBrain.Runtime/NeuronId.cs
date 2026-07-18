namespace DigitalBrain.Runtime;

[GenerateSerializer]
public readonly record struct NeuronId([property: Id(0)] string Value)
{
    public static NeuronId From<T>() => new(typeof(T).FullName!);
    public static NeuronId From(Type type) => new(type.FullName!);
    public override string ToString() => Value;
}
