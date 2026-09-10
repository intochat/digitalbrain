using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Salesforce;

[assembly: NeuronJsonContext(typeof(SalesforceJson))]

namespace DigitalBrain.Salesforce;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RefreshSalesforceConnection))]
[JsonSerializable(typeof(SalesforceRefreshed))]
[JsonSerializable(typeof(SalesforceConnectionRejected))]
[JsonSerializable(typeof(ConnectSalesforceAccount))]
[JsonSerializable(typeof(DisconnectSalesforce))]
[JsonSerializable(typeof(PrepareSalesforceWrite))]
[JsonSerializable(typeof(ConfirmSalesforceWrite))]
[JsonSerializable(typeof(SoqlQuery))]
[JsonSerializable(typeof(SalesforceConnection))]
[JsonSerializable(typeof(SalesforceWritePreview))]
[JsonSerializable(typeof(SalesforceQueryResult))]
[JsonSerializable(typeof(SalesforceUserInfo))]
[JsonSerializable(typeof(Accepted<SalesforceConnection>))]
[JsonSerializable(typeof(Accepted<SalesforceWritePreview>))]
[JsonSerializable(typeof(SalesforceConnected))]
[JsonSerializable(typeof(SalesforceDisconnected))]
[JsonSerializable(typeof(SalesforceWriteRequested))]
[JsonSerializable(typeof(SalesforceWritePrepared))]
[JsonSerializable(typeof(SalesforceWriteConfirmed))]
[JsonSerializable(typeof(RecordWritten))]
[JsonSerializable(typeof(SalesforceWriteFailed))]
[JsonSerializable(typeof(SalesforceWriteUncertain))]
public sealed partial class SalesforceJson : JsonSerializerContext;
