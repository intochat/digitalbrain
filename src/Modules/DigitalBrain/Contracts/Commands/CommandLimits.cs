namespace DigitalBrain.Abstractions.Commands;

public static class CommandLimits
{
    public const int MaxArgumentBytes = 65_536;
    public const int MaxResultBytes = 65_536;
    public const int MaxErrorBytes = 4_096;
}
