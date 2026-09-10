using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Salesforce;

[Alias("salesforce")]
public interface ISalesforce : INeuron
{
    [Alias("connect")]
    Task<Accepted<SalesforceConnection>> Connect(ConnectSalesforceAccount command);

    /// <summary>Requires a stored refresh token; wait for SalesforceRefreshed before retrying a read.</summary>
    [Alias("refresh")]
    Task<Accepted<SalesforceConnection>> Refresh(RefreshSalesforceConnection command);

    [Alias("disconnect")]
    Task<Accepted<SalesforceConnection>> Disconnect(DisconnectSalesforce command);

    /// <summary>Prepares a write preview without changing a record.</summary>
    [Alias("prepare-write")]
    Task<Accepted<SalesforceWritePreview>> PrepareWrite(PrepareSalesforceWrite command);

    /// <summary>Submits the confirmed write preview once.</summary>
    [Alias("confirm-write")]
    Task<Accepted<SalesforceWritePreview>> ConfirmWrite(ConfirmSalesforceWrite command);

    [ReadOnly, Alias("connection")]
    Task<SalesforceConnection> ReadConnection();

    [ReadOnly, Alias("query")]
    Task<SalesforceQueryResult> Query(SoqlQuery query, CancellationToken cancellationToken = default);

    [ReadOnly, Alias("user")]
    Task<SalesforceUserInfo> ReadUserInfo(CancellationToken cancellationToken = default);
}
