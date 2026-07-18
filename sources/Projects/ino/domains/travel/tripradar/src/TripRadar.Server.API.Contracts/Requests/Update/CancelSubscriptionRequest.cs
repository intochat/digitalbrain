using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Update;

public class CancelSubscriptionRequest
{
    [JsonPropertyName("cancellationReason")]
    [StringLength(500, ErrorMessage = "Cancellation reason must not exceed 500 characters.")]
    public string? CancellationReason { get; set; }
}
