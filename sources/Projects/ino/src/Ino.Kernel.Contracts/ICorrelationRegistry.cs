using Orleans;

namespace Ino.Kernel.Contracts;

/// <summary>
/// Maps a chat correlation_id back to the plan grain that authored the
/// outbound RFW payload. The gateway calls <see cref="RegisterAsync"/> when
/// it stamps an RFW response with a fresh correlation_id, and
/// <see cref="GetAsync"/> when the client fires an RfwEvent so it can
/// resolve which plan grain receives the callback.
///
/// We store the plan's typed interface (assembly-qualified name) plus the
/// grain key rather than an opaque <c>GrainId</c> — when the gateway resolves
/// the addressable, the kernel silo's grain manifest must know how to
/// activate a reference for that interface, which it does only for
/// interfaces declared in assemblies it references. Going through the typed
/// interface keeps the activation path inside what the kernel manifest
/// already covers.
///
/// In-memory and volatile — silo restart drops in-flight trip correlations,
/// matching the v0.1 scope. Persistence is tracked under issue #22.
/// </summary>
public interface ICorrelationRegistry : IGrainWithStringKey
{
    Task RegisterAsync(string correlationId, string planInterfaceAqn, string grainKey);

    Task<CorrelationEntry?> GetAsync(string correlationId);
}

[GenerateSerializer]
public sealed record CorrelationEntry(
    [property: Id(0)] string PlanInterfaceAqn,
    [property: Id(1)] string GrainKey);
