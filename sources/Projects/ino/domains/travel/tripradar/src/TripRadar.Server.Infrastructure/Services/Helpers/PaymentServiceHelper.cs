using TripRadar.Server.Domain.Enums;
using TripRadar.Server.Infrastructure.Constants;
using System.Globalization;

namespace TripRadar.Server.Infrastructure.Services.Helpers;

public static class PaymentServiceHelper
{
    private const string DefaultStripeRefundReason = "requested_by_customer";

    private static readonly Dictionary<string, string> _refundReasonMappings = new()
    {
        [nameof(RefundType.RequestedByCustomer)] = "requested_by_customer",
        [nameof(RefundType.Duplicate)] = "duplicate",
        [nameof(RefundType.Fraudulent)] = "fraudulent",
        [nameof(RefundType.SubscriptionCanceled)] = "requested_by_customer",
        [nameof(RefundType.ServiceNotDelivered)] = "requested_by_customer"
    };

    public static string GetStripeCompatibleRefundReason(RefundType type) => _refundReasonMappings.GetValueOrDefault(type.Name, DefaultStripeRefundReason);

    public static Dictionary<string, string> BuildRefundMetadata(Dictionary<string, string>? metadata)
    {
        var refundMetadata = metadata ?? new Dictionary<string, string>();
        refundMetadata.TryAdd(SubscriptionConstants.Metadata.ProcessedByKey, SubscriptionConstants.Metadata.ProcessedByValue);
        refundMetadata.TryAdd(SubscriptionConstants.Metadata.ProcessedAtKey, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        return refundMetadata;
    }
}
