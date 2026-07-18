using System.Text;

namespace TripRadar.Bot.Notifications.Format;

internal sealed class NotificationEnvelopeRenderer
{
    private const int MaxDetailLines = 3;

    public string Render(NotificationEnvelope envelope)
    {
        var sb = new StringBuilder();
        sb.Append(NotificationStrings.Header).Append(": ").AppendLine(envelope.TypeLabel);
        sb.AppendLine(envelope.RequestSummary);
        sb.AppendLine(envelope.MainResult);

        var detailsAdded = 0;
        foreach (var detail in envelope.Details)
        {
            if (detailsAdded >= MaxDetailLines)
                break;
            if (string.IsNullOrWhiteSpace(detail))
                continue;
            sb.AppendLine(detail);
            detailsAdded++;
        }

        sb.Append(NotificationStrings.Cta);
        return sb.ToString();
    }
}
