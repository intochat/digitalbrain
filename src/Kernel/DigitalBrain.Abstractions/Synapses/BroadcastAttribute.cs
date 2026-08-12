namespace DigitalBrain.Abstractions;

// Opt-in: only synapses marked with this attribute mint per-correlation broadcast
// receivers (type:owner/{correlation}) on Emit. IHandle alone no longer enrolls a
// fact in the broadcast catalog — directed Send and the synapse graph are the
// product delivery paths. See Wave 1 / kernel trap 8.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BroadcastAttribute : Attribute;
