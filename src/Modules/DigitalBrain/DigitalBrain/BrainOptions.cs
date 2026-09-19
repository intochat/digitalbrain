namespace DigitalBrain.Core;
public sealed class BrainOptions
{
    public int BufferCapacity { get; set; } = 256;
    public TimeSpan ObserverLease { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan RenewEvery { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
