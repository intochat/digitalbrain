namespace DigitalBrain.Kernel.Kernel;

using DigitalBrain.Core;

// Kernel task grain contract (kernel-owned). Task messages are now universal core protocol (TaskCreated etc.).
// Kernel layer owns the durable execution grain.
[Alias("DigitalBrain.Kernel.Kernel.IKernelTask")]
public interface IKernelTask : INeuron, IHandle<RunTask>, IHandle<CancelTask>
{
    [Alias("GetInfoAsync")]
    Task<TaskInfo> GetInfoAsync();
}
