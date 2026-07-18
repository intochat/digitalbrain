using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Requests.Update;

/// <summary>
/// Request to toggle subscription activation state.
/// </summary>
public sealed class ToggleSubscriptionRequest
{
    /// <summary>
    /// When true, removes scheduled cancellation (reactivates).
    /// When false, schedules subscription for cancellation at period end.
    /// </summary>
    [Required]
    [JsonPropertyName("activate")]
    [DataMember(Name = "activate")]
    public bool Activate { get; set; }
}
