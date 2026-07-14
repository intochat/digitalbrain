namespace DigitalBrain.Kernel.Contracts;

[GenerateSerializer]
[Alias("DigitalBrain.Kernel.Contracts.UserId")]
public readonly record struct UserId([property: Id(0)] string Value)
{
    public static UserId Anonymous => new("anonymous");
}
