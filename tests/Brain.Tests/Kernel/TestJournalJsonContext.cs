using System.Text.Json.Serialization;
using Brain.Contracts;
using Brain.Kernel;

namespace Brain.Tests.Kernel;

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
[JsonSerializable(typeof(ProbeDomainEvent))]
[JsonSerializable(typeof(EventSynapse<ProbeDomainEvent>))]
[JsonSerializable(typeof(OutboxIntent<ProbeDomainEvent>))]
[JsonSerializable(typeof(EventSynapse<string>))]
[JsonSerializable(typeof(OutboxIntent<string>))]
public sealed partial class TestJournalJsonContext : JsonSerializerContext;
