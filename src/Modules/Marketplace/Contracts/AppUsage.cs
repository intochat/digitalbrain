namespace DigitalBrain.Marketplace;

// Reviews may only come from a workspace that actually ran the app through the broker. The ledger
// records that fact; the catalog consults it before accepting a review.
public interface IAppUsageLedger
{
    void RecordRun(string appId, string workspaceId);

    bool HasRun(string appId, string workspaceId);
}
