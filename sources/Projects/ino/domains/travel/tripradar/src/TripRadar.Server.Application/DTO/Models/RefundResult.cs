namespace TripRadar.Server.Application.DTO.Models;

/// <summary>
/// Represents the result of a successful refund operation.
/// </summary>
public record RefundResult(string RefundId, string PaymentIntentId, decimal Amount, string Currency, string Status, string Reason, DateTime Created, Dictionary<string, string>? Metadata);
