namespace DigitalBrain.Client.Orleans;

public sealed class SubscriptionOptions
{
    public int BufferCapacity { get; set; } = 256;
    public TimeSpan RenewEvery { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
