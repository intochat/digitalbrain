using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Salesforce;

[Alias("salesforce")]
public interface ISalesforce : INeuron
{
    /// <summary>Redeems a one-use login nonce; the instance URL must be HTTPS.</summary>
    Task<SalesforceConnection> Connect(ConnectSalesforceAccount account);

    /// <summary>Requires a stored refresh token; a refused grant clears the connection.</summary>
    Task<SalesforceConnection> Refresh(RefreshSalesforceConnection command);

    Task<SalesforceConnection> Disconnect(DisconnectSalesforce command);

    /// <summary>Prepares a write preview without changing a record.</summary>
    Task<SalesforceWritePreview> PrepareWrite(PrepareSalesforceWrite command);

    /// <summary>Submits the confirmed write preview once.</summary>
    Task<SalesforceWritePreview> ConfirmWrite(ConfirmSalesforceWrite command);

    [ReadOnly]
    Task<SalesforceConnection> ReadConnection();

    [ReadOnly]
    Task<SalesforceQueryResult> Query(SoqlQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<SalesforceSchema> ReadSchema(ReadSalesforceSchema query, CancellationToken cancellationToken = default);

    [ReadOnly]
    Task<SalesforceUserInfo> ReadUserInfo(CancellationToken cancellationToken = default);
}