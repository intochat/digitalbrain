using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common
{
    public sealed record CreateScheduledQueryResponse(
        [property: JsonPropertyName("scheduledExecutionUniqueId")] Guid UniqueId
    );
}