using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Salesforce;

[Alias("salesforce")]
public interface ISalesforce : INeuron
{
    [Alias("connect")]
    [NeuronTool]
    Task<Accepted<SignalId>> Connect(ConnectSalesforceAccount command);

    /// <summary>Requires a stored refresh token; wait for SalesforceRefreshed before retrying a read.</summary>
    [Alias("refresh")]
    [NeuronTool]
    Task<Accepted<SalesforceConnection>> Refresh(RefreshSalesforceConnection command);

    [Alias("disconnect")]
    [NeuronTool]
    Task<Accepted<SalesforceConnection>> Disconnect(DisconnectSalesforce command);

    /// <summary>Prepares a write preview without changing a record.</summary>
    [Alias("prepare-write")]
    [NeuronTool]
    Task<Accepted<SalesforceWritePreview>> PrepareWrite(PrepareSalesforceWrite command);

    /// <summary>Submits the confirmed write preview once.</summary>
    [Alias("confirm-write")]
    [NeuronTool]
    Task<Accepted<SalesforceWritePreview>> ConfirmWrite(ConfirmSalesforceWrite command);

    [ReadOnly, Alias("connection")]
    [NeuronTool(IsReadOnly = true)]
    Task<SalesforceConnection> ReadConnection();

    [ReadOnly, Alias("query")]
    [NeuronTool(IsReadOnly = true)]
    Task<SalesforceQueryResult> Query(SoqlQuery query, CancellationToken cancellationToken = default);

    [ReadOnly, Alias("schema")]
    [NeuronTool(IsReadOnly = true)]
    Task<SalesforceSchema> ReadSchema(ReadSalesforceSchema query, CancellationToken cancellationToken = default);

    [ReadOnly, Alias("user")]
    [NeuronTool(IsReadOnly = true)]
    Task<SalesforceUserInfo> ReadUserInfo(CancellationToken cancellationToken = default);
}
