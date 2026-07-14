using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Kernel.Memory;
namespace DigitalBrain.Kernel.Capabilities;

public sealed record CapabilityIntentBinding(string CapabilityId, int CapabilityVersion, CapabilityOperationKind Kind);

internal sealed class BuiltInCapabilityCatalog : ICapabilityCatalog
{
    internal const string AssistantAnswerCapabilityId = "assistant.answer";
    private const string GmailSendEffectGrant = "gmail.send";
    private const string SalesforceUpdateEffectGrant = "salesforce.record.update";
    private const string GoogleConnection = "google";
    private const string SalesforceConnection = "salesforce";
    private static readonly IReadOnlyDictionary<string, CapabilityIntentBinding> Bindings = new Dictionary<string, CapabilityIntentBinding>(StringComparer.Ordinal)
    {
        [AssistantAnswerCapabilityId] = new(AssistantAnswerCapabilityId, 1, CapabilityOperationKind.Query),
        [GoogleCapabilityIds.GmailMessageRead] = new(GoogleCapabilityIds.GmailMessageRead, 1, CapabilityOperationKind.Query),
        [GoogleCapabilityIds.GmailMailboxRead] = new(GoogleCapabilityIds.GmailMailboxRead, 1, CapabilityOperationKind.Query),
        [GoogleCapabilityIds.GmailSendPropose] = new(GoogleCapabilityIds.GmailSendPropose, 1, CapabilityOperationKind.ExternalEffect),
        [SalesforceCapabilityIds.RecordRead] = new(SalesforceCapabilityIds.RecordRead, 1, CapabilityOperationKind.Query),
        [SalesforceCapabilityIds.RecordUpdatePropose] = new(SalesforceCapabilityIds.RecordUpdatePropose, 1, CapabilityOperationKind.ExternalEffect),
        [MemoryCapabilityIds.Recall] = new(MemoryCapabilityIds.Recall, 1, CapabilityOperationKind.Query),
        [MemoryCapabilityIds.Remember] = new(MemoryCapabilityIds.Remember, 1, CapabilityOperationKind.InternalWrite)
    };
    public static bool TryBind(string capabilityId, out CapabilityIntentBinding binding) =>
        Bindings.TryGetValue(capabilityId, out binding!);
    public IReadOnlyList<CapabilityDescriptor> Snapshot() =>
        Bindings.Keys.Order(StringComparer.Ordinal).Select(CreateDescriptor).ToArray();
    internal static CapabilityDescriptor CreateDescriptor(string capabilityId) => capabilityId switch
    {
        AssistantAnswerCapabilityId => new CapabilityDescriptor(
            AssistantAnswerCapabilityId,
            1,
            "Assistant answer",
            "Answers the user directly from the assistant's own reasoning without reading or changing any external system.",
            ["Explain what a capability grant is.", "What time zone is Kyiv in?"],
            [],
            [],
            CapabilityOrigin.Platform,
            CapabilityOperationKind.Query,
            true),
        GoogleCapabilityIds.GmailMessageRead => new CapabilityDescriptor(
            GoogleCapabilityIds.GmailMessageRead,
            1,
            "Read a Gmail message",
            "Reads the full content of a single Gmail message the user already received.",
            ["Show me the email from my landlord about the lease renewal.", "Open the latest message from Anna."],
            [],
            [GoogleConnection],
            CapabilityOrigin.Integration,
            CapabilityOperationKind.Query,
            true),
        GoogleCapabilityIds.GmailMailboxRead => new CapabilityDescriptor(
            GoogleCapabilityIds.GmailMailboxRead,
            1,
            "List Gmail mailbox messages",
            "Lists and searches messages in the user's Gmail mailbox.",
            ["What unread emails do I have from this week?", "Find invoices in my inbox from Acme Corp."],
            [],
            [GoogleConnection],
            CapabilityOrigin.Integration,
            CapabilityOperationKind.Query,
            true),
        GoogleCapabilityIds.GmailSendPropose => new CapabilityDescriptor(
            GoogleCapabilityIds.GmailSendPropose,
            1,
            "Propose sending a Gmail message",
            "Drafts an outgoing Gmail message as a proposal that the user must approve before anything is sent.",
            ["Send a follow-up email to the recruiter thanking them for the interview.", "Email my landlord that I will renew the lease."],
            [GmailSendEffectGrant],
            [GoogleConnection],
            CapabilityOrigin.Integration,
            CapabilityOperationKind.ExternalEffect,
            true),
        SalesforceCapabilityIds.RecordRead => new CapabilityDescriptor(
            SalesforceCapabilityIds.RecordRead,
            1,
            "Read a Salesforce record",
            "Reads a single Salesforce record and its fields.",
            ["What is the current stage of the Acme opportunity?", "Show me the contact details for Jane Doe in Salesforce."],
            [],
            [SalesforceConnection],
            CapabilityOrigin.Integration,
            CapabilityOperationKind.Query,
            true),
        SalesforceCapabilityIds.RecordUpdatePropose => new CapabilityDescriptor(
            SalesforceCapabilityIds.RecordUpdatePropose,
            1,
            "Propose a Salesforce record update",
            "Prepares a change to one Salesforce record field as a proposal that the user must approve before it is applied.",
            ["Move the Acme opportunity to Closed Won.", "Update the phone number on the Globex account."],
            [SalesforceUpdateEffectGrant],
            [SalesforceConnection],
            CapabilityOrigin.Integration,
            CapabilityOperationKind.ExternalEffect,
            true),
        MemoryCapabilityIds.Recall => new CapabilityDescriptor(
            MemoryCapabilityIds.Recall,
            1,
            "Recall remembered facts",
            "Searches facts the user previously asked the brain to remember.",
            ["What did I say my shirt size was?", "Do you remember my home Wi-Fi name?"],
            [],
            [],
            CapabilityOrigin.Platform,
            CapabilityOperationKind.Query,
            true),
        MemoryCapabilityIds.Remember => new CapabilityDescriptor(
            MemoryCapabilityIds.Remember,
            1,
            "Remember a fact",
            "Stores a fact in the user's private memory for later recall.",
            ["Remember that my passport expires in March 2027.", "Note that Anna prefers morning meetings."],
            [],
            [],
            CapabilityOrigin.Platform,
            CapabilityOperationKind.InternalWrite,
            true),
        _ => throw new ArgumentOutOfRangeException(nameof(capabilityId), capabilityId, "The capability id has no built-in descriptor.")
    };
}
