using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Kernel.Capabilities;
namespace DigitalBrain.Integrations.Google;

internal sealed class GoogleCapabilityDescriptorSource : ICapabilityDescriptorSource
{
    private const string GoogleConnection = "google";
    public IReadOnlyList<CapabilityDescriptor> Descriptors { get; } =
    [
        new CapabilityDescriptor(
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
        new CapabilityDescriptor(
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
        new CapabilityDescriptor(
            GoogleCapabilityIds.GmailSendPropose,
            1,
            "Propose sending a Gmail message",
            "Drafts an outgoing Gmail message as a proposal that the user must approve before anything is sent.",
            ["Send a follow-up email to the recruiter thanking them for the interview.", "Email my landlord that I will renew the lease."],
            [GmailTools.Send],
            [GoogleConnection],
            CapabilityOrigin.Integration,
            CapabilityOperationKind.ExternalEffect,
            true)
    ];
}
