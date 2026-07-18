using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common
{
    public sealed record ScheduledExecutionsResponse(
        [property: JsonPropertyName("scheduledExecutions")] List<ScheduledExecution> ScheduledExecutions
    );
}