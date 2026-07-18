using Orleans.Runtime;

namespace Ino.Core.Hosting.Brain;

/// <summary>
/// Memory-streams provider config for the brain pulse channel. Distinct
/// provider name from IAW's "agents" stream so a downstream switch from
/// memory → Azure Storage Queues (spec §8 open question) doesn't disturb
/// IAW's existing channel. Single shared stream id — every silo's
/// <c>BrainTraceFilter</c> writes here, every <c>WatchBrainActivity</c>
/// subscriber reads here.
/// </summary>
public static class InoBrainStream
{
    public const string ProviderName = "ino-brain";
    public const string Namespace = "brain";
    public const string Key = "pulses";

    public static StreamId Id { get; } = StreamId.Create(Namespace, Key);
}
