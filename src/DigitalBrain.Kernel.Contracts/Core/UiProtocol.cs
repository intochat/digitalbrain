namespace DigitalBrain.Kernel.Contracts.Runtime;

public static class UiProtocol
{
    public const int ProtocolVersion = 2;
    public const string SurfaceSchema = "digitalbrain.surface";
    public const int SurfaceSchemaVersion = 2;
    public const int ActionSchemaVersion = 1;
    public static readonly TimeSpan ActionTokenLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SurfaceLifetime = TimeSpan.FromHours(24);
}
