namespace DigitalBrain.Kernel.Visualization;

public sealed class TaskManagerOptions
{
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(8);
    public int MaxTracked { get; set; } = 200;
}
