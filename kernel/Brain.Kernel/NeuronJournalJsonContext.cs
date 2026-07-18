using Brain.Contracts;
using System.Text.Json.Serialization;

namespace Brain.Kernel;

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(NeuronStatus))]
[JsonSerializable(typeof(ExternalOperationStatus))]
[JsonSerializable(typeof(NeuronFailureKind))]
[JsonSerializable(typeof(ExternalOperation))]
[JsonSerializable(typeof(NeuronNotification))]
public sealed partial class NeuronJournalJsonContext : JsonSerializerContext;
