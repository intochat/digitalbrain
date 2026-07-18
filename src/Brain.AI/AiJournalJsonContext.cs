namespace DigitalBrain.AI;

using System.Text.Json.Serialization;
using Brain.Contracts;
using Brain.Kernel;

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(CommandReceipt))]
[JsonSerializable(typeof(CommandReceiptStatus))]
[JsonSerializable(typeof(SanitizedFailure))]
[JsonSerializable(typeof(OrganizationId))]
[JsonSerializable(typeof(PrincipalId))]
[JsonSerializable(typeof(SpaceId))]
[JsonSerializable(typeof(NeuronAddress))]
[JsonSerializable(typeof(SynapseMetadata))]
[JsonSerializable(typeof(EventSynapse<string>))]
[JsonSerializable(typeof(OutboxIntent<string>))]
[JsonSerializable(typeof(GroupChatStepEvent))]
[JsonSerializable(typeof(EventSynapse<GroupChatStepEvent>))]
[JsonSerializable(typeof(OutboxIntent<GroupChatStepEvent>))]
[JsonSerializable(typeof(UiFeedCandidate))]
[JsonSerializable(typeof(EventSynapse<UiFeedCandidate>))]
[JsonSerializable(typeof(UiFeedFrame))]
[JsonSerializable(typeof(UiFeedPage))]
[JsonSerializable(typeof(UiSurface))]
[JsonSerializable(typeof(UiSurfaceSnapshot))]
[JsonSerializable(typeof(UiBlock))]
[JsonSerializable(typeof(UiAction))]
[JsonSerializable(typeof(AgentTurnRequest))]
[JsonSerializable(typeof(AgentTurnResult))]
public sealed partial class AiJournalJsonContext : JsonSerializerContext;
