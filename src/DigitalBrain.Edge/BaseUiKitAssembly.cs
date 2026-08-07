using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Product.Approvals;
using DigitalBrain.Product.Presentation;
using DigitalBrain.Product.SalesInsights;

namespace DigitalBrain.Edge;

internal static class BaseUiKitAssembly
{
    internal static UiSurface Approvals(ApprovalWorkspaceSurfaceRequested approval)
    {
        ArgumentNullException.ThrowIfNull(approval);

        var cards = new List<string>(approval.Items.Count);
        var components = new List<BaseUiKitComponent>();
        foreach (var item in approval.Items)
        {
            var cardId = OpaqueId("approval-card-v1", item.ProposalId, item.ProposalFingerprint);
            cards.Add(cardId);
            components.Add(new CardComponent(cardId, item.Title, item.Summary));
            components.Add(new StatusComponent(cardId, ApprovalStatus(item.Status)));
            components.Add(new EvidenceComponent(cardId, [.. item.Evidence.Select(Evidence)]));
            components.Add(new ChangesComponent(cardId, [.. item.Changes.Select(Change)]));

            if (item.Context is { Kind: ApprovalReviewContextKind.ChatConversation } context)
            {
                if (item.Placements.Contains(ApprovalReviewPlacement.Chat))
                {
                    components.Add(new ChatComponent(context.OpaqueContextRef, cardId));
                }

                if (item.Placements.Contains(ApprovalReviewPlacement.ContextDrawer))
                {
                    components.Add(new DrawerComponent(context.OpaqueContextRef, cardId));
                }
            }

            if (item.Status == ApprovalWorkspaceItemStatus.Pending)
            {
                foreach (var action in item.Actions)
                {
                    components.Add(new ActionComponent(
                        cardId,
                        ActionLabel(action.Decision),
                        new OpaqueUiActionReference(action.Reference)));
                }
            }
        }

        components.Insert(0, new InboxComponent("Approvals", cards));
        return new UiSurface("approvals", approval.Revision, components);
    }

    internal static UiSurface SalesReady(SalesInsightSurfaceRequested sales, long position)
    {
        ArgumentNullException.ThrowIfNull(sales);
        var points = sales.Buckets
            .Select(static bucket => new UiChartPoint(bucket.Date, bucket.Amount, bucket.ClosedDealCount))
            .ToArray();
        var rows = sales.Buckets
            .Select(static bucket => new UiTableRow(bucket.Date, bucket.Amount, bucket.ClosedDealCount))
            .ToArray();
        return new UiSurface(
            OpaqueId("sales-surface-v1", sales.QueryId),
            position,
            [
                new BarChartComponent("Closed won revenue", sales.CurrencyCode, points),
                new TableComponent(
                    "Closed won revenue",
                    sales.CurrencyCode,
                    rows,
                    sales.TotalAmount,
                    sales.ClosedDealCount),
            ]);
    }

    internal static UiSurface SalesUnavailable(SalesInsightUnavailableSurfaceRequested sales, long position)
    {
        ArgumentNullException.ThrowIfNull(sales);
        return new UiSurface(
            OpaqueId("sales-surface-v1", sales.QueryId),
            position,
            [new UnavailableComponent("Sales data unavailable", UnavailableReason(sales.Reason))]);
    }

    private static UiEvidence Evidence(ApprovalEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return new UiEvidence(evidence.Source, evidence.Summary, SafeReference(evidence.ReferenceUri));
    }

    private static UiChange Change(ApprovalChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return new UiChange(change.Field, change.Before, change.After);
    }

    private static string ActionLabel(ApprovalReviewDecision decision)
        => decision switch
        {
            ApprovalReviewDecision.Approve => "Approve",
            ApprovalReviewDecision.Reject => "Reject",
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "The approval action is not recognized."),
        };

    private static string ApprovalStatus(ApprovalWorkspaceItemStatus status)
        => status switch
        {
            ApprovalWorkspaceItemStatus.Pending => "Pending",
            ApprovalWorkspaceItemStatus.Approved => "Approved",
            ApprovalWorkspaceItemStatus.Rejected => "Rejected",
            ApprovalWorkspaceItemStatus.Expired => "Expired",
            ApprovalWorkspaceItemStatus.MutationUncertain => "MutationUncertain",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "The approval status is not recognized."),
        };

    private static string UnavailableReason(SalesInsightUnavailableReason reason)
        => Enum.IsDefined(reason) ? reason.ToString() : "Unavailable";

    private static string OpaqueId(string version, params string[] values)
    {
        var material = string.Join('\0', [version, .. values]);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return "ui_" + Convert.ToHexString(digest);
    }

    private static string? SafeReference(Uri? reference)
    {
        if (reference is null
            || !reference.IsAbsoluteUri
            || !string.Equals(reference.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(reference.DnsSafeHost)
            || !string.IsNullOrEmpty(reference.UserInfo)
            || reference.IsLoopback
            || System.Net.IPAddress.TryParse(reference.Host, out _)
            || string.Equals(reference.DnsSafeHost, "localhost", StringComparison.OrdinalIgnoreCase)
            || reference.DnsSafeHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new UriBuilder
        {
            Scheme = Uri.UriSchemeHttps,
            Host = reference.DnsSafeHost,
            Port = -1,
            Path = reference.AbsolutePath,
            Query = string.Empty,
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty,
        }.Uri.AbsoluteUri;
    }
}
