using Ino.Core.Brain;

namespace Ino.Core.Hosting.Brain;

/// One-line abstraction over Orleans Streams so the filter can be unit-tested
/// without a TestCluster. Production binding lives in
/// <c>InoBrainStreamingExtensions</c>.
public interface IBrainPulseSink
{
    Task EmitAsync(BrainPulse pulse, CancellationToken ct);
}
