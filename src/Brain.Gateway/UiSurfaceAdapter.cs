using Brain.Contracts;
using DigitalBrain.AI;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;

namespace Brain.Gateway;

public static class UiSurfaceAdapter
{
    private const string GroupChatContractId = "chat.group.v1";
    private const string GmailContractId = "google.gmail.v1";
    private const string SalesforceContractId = "salesforce.v1";

    public static Task<UiSurfaceSnapshot> GetAsync(
        string surfaceGrainKey,
        DevelopmentPrincipal principal,
        Func<string, IGroupChat> resolveGroupChat,
        Func<string, IGmail> resolveGmail,
        Func<string, ISalesforce> resolveSalesforce)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(resolveGroupChat);
        ArgumentNullException.ThrowIfNull(resolveGmail);
        ArgumentNullException.ThrowIfNull(resolveSalesforce);

        var surfaceAddress = NeuronAddress.Parse(surfaceGrainKey);
        if (surfaceAddress.OrganizationId != principal.OrganizationId
            || surfaceAddress.SpaceId != principal.SpaceId)
        {
            throw new InvalidOperationException("Surface is not authorized for UI surface.");
        }

        return surfaceAddress.ContractId switch
        {
            GroupChatContractId => resolveGroupChat(surfaceGrainKey).GetSurfaceAsync(),
            GmailContractId => resolveGmail(surfaceGrainKey).GetSurfaceAsync(),
            SalesforceContractId => resolveSalesforce(surfaceGrainKey).GetSurfaceAsync(),
            _ => throw new InvalidOperationException("Surface contract is not supported."),
        };
    }
}
