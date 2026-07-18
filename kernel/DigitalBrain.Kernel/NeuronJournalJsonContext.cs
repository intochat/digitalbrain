using DigitalBrain;
using System.Text.Json.Serialization;

namespace DigitalBrain.Kernel;

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
[JsonSerializable(typeof(NotificationDeliveryStatus))]
[JsonSerializable(typeof(ExternalOperation))]
[JsonSerializable(typeof(NeuronNotification))]
[JsonSerializable(typeof(ConversationRole))]
[JsonSerializable(typeof(ConversationTurnRequest))]
[JsonSerializable(typeof(ConversationTurn))]
[JsonSerializable(typeof(ConversationTurnResult))]
public sealed partial class NeuronJournalJsonContext : JsonSerializerContext;
