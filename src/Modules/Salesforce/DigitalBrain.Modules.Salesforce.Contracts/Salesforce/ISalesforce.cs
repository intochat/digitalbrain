using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Salesforce;

[Alias("salesforce")]
public interface ISalesforce : INeuron
{
    // Redeems a one-use login nonce; the instance URL must be HTTPS.
    Task<SalesforceConnection> Connect(ConnectSalesforceAccount account);

    // Requires a stored refresh token; a refused grant clears the connection.
    Task<SalesforceConnection> Refresh(RefreshSalesforceConnection command);

    Task<SalesforceConnection> Disconnect(DisconnectSalesforce command);

    Task<SalesforceWritePreview> PrepareWrite(PrepareSalesforceWrite command);

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