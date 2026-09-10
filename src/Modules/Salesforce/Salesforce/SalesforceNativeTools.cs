using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceNativeTools(ISalesforce salesforce)
{
    public Task<SalesforceUserInfo> GetUserInfo(CancellationToken cancellationToken = default)
        => salesforce.ReadUserInfo(cancellationToken);

    public Task<SalesforceQueryResult> SoqlQuery(SoqlQuery query, CancellationToken cancellationToken = default)
        => salesforce.Query(query, cancellationToken);

    public Task<Accepted<SalesforceWritePreview>> CreateRecord(string arguments)
        => salesforce.PrepareWrite(new PrepareSalesforceWrite(CommandId.New(), "createRecord", arguments));

    public Task<Accepted<SalesforceWritePreview>> UpdateRecord(string arguments)
        => salesforce.PrepareWrite(new PrepareSalesforceWrite(CommandId.New(), "updateRecord", arguments));
}
