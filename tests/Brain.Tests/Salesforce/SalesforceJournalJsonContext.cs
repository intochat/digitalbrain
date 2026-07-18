using System.Text.Json.Serialization;
using Brain.Contracts;
using Brain.Kernel;
using DigitalBrain.Salesforce;

namespace Brain.Tests.Salesforce;

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
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(SalesforceFeedEvent))]
[JsonSerializable(typeof(EventSynapse<SalesforceFeedEvent>))]
[JsonSerializable(typeof(OutboxIntent<SalesforceFeedEvent>))]
public sealed partial class SalesforceJournalJsonContext : JsonSerializerContext;
