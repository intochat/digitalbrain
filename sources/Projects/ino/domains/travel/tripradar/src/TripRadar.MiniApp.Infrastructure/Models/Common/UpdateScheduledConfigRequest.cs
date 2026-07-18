using System.Text.Json.Serialization;

namespace TripRadar.MiniApp.Client.Infrastructure.Models.Common
{
    public sealed record UpdateScheduledConfigRequest(
        [property: JsonPropertyName("isActive")] bool IsActive,
        [property: JsonPropertyName("schedule")] string Schedule,
        [property: JsonPropertyName("nextExecutionTime")] DateTime NextExecutionTime
    );
}