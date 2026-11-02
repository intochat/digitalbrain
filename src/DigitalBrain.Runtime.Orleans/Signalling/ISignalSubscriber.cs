using Orleans;
using Orleans.Concurrency;

namespace DigitalBrain.Runtime.Orleans.Signalling;

/// <summary>A grain that receives fire-and-forget signal log position notifications.</summary>
[Alias("signal.subscriber")]
public interface ISignalSubscriber : IGrainWithStringKey
{
    /// <summary>Fire-and-forget notification of a newly committed brain log position N.</summary>
    [OneWay]
    Task Notify(long position);
}
