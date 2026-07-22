using DigitalBrain.Abstractions;

namespace DigitalBrain.Salesforce;

[Alias("db.salesforce")]
public interface ISalesforce : INeuron
{
    [Alias("ProposeAccountDescription")]
    Task<SalesforceAccountDescriptionMutation> ProposeAccountDescriptionAsync(
        CommandId commandId,
        string accountId,
        string description);

    [Alias("ApproveAccountDescription")]
    Task<SalesforceAccountDescriptionMutation> ApproveAccountDescriptionAsync(
        CommandId commandId,
        string fingerprint);
}
