namespace DigitalBrain.Salesforce;

using Brain.Contracts;

[Alias("digitalbrain.salesforce.ISalesforce")]
[NeuronContract("salesforce.v1")]
public interface ISalesforce : IGrainWithStringKey
{
    [Alias("GetIdentityAsync")]
    Task<string> GetIdentityAsync();

    [Alias("QueryRecordsAsync")]
    Task<CommandReceipt> QueryRecordsAsync(CommandSynapse<SalesforceQueryRequest> command);

    [Alias("UpdateRecordAsync")]
    Task<CommandReceipt> UpdateRecordAsync(CommandSynapse<SalesforceUpdateRequest> command);

    [Alias("GetSurfaceAsync")]
    Task<UiSurfaceSnapshot> GetSurfaceAsync();
}

[GenerateSerializer, Alias("brain.salesforce.query-request.v1")]
public sealed record SalesforceQueryRequest(
    [property: Id(0)] string Soql);

[GenerateSerializer, Alias("brain.salesforce.update-request.v1")]
public sealed record SalesforceUpdateRequest(
    [property: Id(0)] string ObjectType,
    [property: Id(1)] string RecordId,
    [property: Id(2)] IReadOnlyDictionary<string, string> Fields);
