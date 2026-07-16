using Brain.Contracts;
using System.Text.Json.Serialization;

namespace Brain.Kernel;

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(NeuronEvent))]
[JsonSerializable(typeof(NeuronReceipt))]
[JsonSerializable(typeof(SynapseRecord))]
[JsonSerializable(typeof(SynapseRelation))]
public sealed partial class NeuronJournalJsonContext : JsonSerializerContext;
