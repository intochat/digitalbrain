namespace Ino.Core.Hosting;

public static class Telemetry
{
    public const string ActivitySourceName = "ino";
    public const string MeterName = "ino";

    public static class Tags
    {
        public const string SynapseType                 = "ino.synapse.type";
        public const string SourceDomain                = "ino.source.domain";
        public const string TargetDomain                = "ino.target.domain";
        public const string CorrelationId               = "ino.correlation_id";
        public const string ResultSuccess               = "ino.result.success";
        public const string ErrorCode                   = "ino.error.code";
        public const string BroadcastTargetCount        = "ino.broadcast.target_count";
        public const string BroadcastFailedCount        = "ino.broadcast.failed_count";
        public const string BroadcastCapabilityDenied   = "ino.broadcast.capability_denied_count";
        public const string BroadcastTransportFailures  = "ino.broadcast.transport_failure_count";
        public const string SynapseArgPrefix            = "ino.synapse.arg.";
    }

    public static class Spans
    {
        public static string Fire(Type synapseType)           => $"fire {synapseType.FullName}";
        public static string FireBroadcast(Type synapseType)  => $"fire-broadcast {synapseType.FullName}";
        public static string Handle(Type synapseType)         => $"handle {synapseType.FullName}";
        public static string React(Type synapseType)          => $"react {synapseType.FullName}";
    }
}
