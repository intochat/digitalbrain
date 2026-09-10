using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Salesforce;

[Alias("salesforce")]
public interface ISalesforce : INeuron
{
    /// <summary>Connects the selected Salesforce account.</summary>
    [Alias("connect")]
    Task<Accepted<SalesforceConnection>> Connect(ConnectSalesforceAccount command);

    /// <summary>Disconnects the selected Salesforce account.</summary>
    [Alias("disconnect")]
    Task<Accepted<SalesforceConnection>> Disconnect(DisconnectSalesforce command);

    /// <summary>Prepares a write preview without changing a record.</summary>
    [Alias("prepare-write")]
    Task<Accepted<SalesforceWritePreview>> PrepareWrite(PrepareSalesforceWrite command);

    /// <summary>Submits the confirmed write preview once.</summary>
    [Alias("confirm-write")]
    Task<Accepted<SalesforceWritePreview>> ConfirmWrite(ConfirmSalesforceWrite command);

    /// <summary>Reads the stored Salesforce connection.</summary>
    [ReadOnly, Alias("connection")]
    Task<SalesforceConnection> ReadConnection();

    /// <summary>Runs a guarded Salesforce SELECT query.</summary>
    [ReadOnly, Alias("query")]
    Task<SalesforceQueryResult> Query(SoqlQuery query, CancellationToken cancellationToken = default);

    /// <summary>Reads the connected Salesforce user.</summary>
    [ReadOnly, Alias("user")]
    Task<SalesforceUserInfo> ReadUserInfo(CancellationToken cancellationToken = default);
}
