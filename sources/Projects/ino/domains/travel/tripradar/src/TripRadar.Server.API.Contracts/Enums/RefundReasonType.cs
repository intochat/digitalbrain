using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RefundReasonType
{
    [EnumMember(Value = "RequestedByCustomer")] [Description("RequestedByCustomer")]
    RequestedByCustomer = 1,

    [EnumMember(Value = "Duplicate")] [Description("Duplicate")]
    Duplicate = 2,

    [EnumMember(Value = "Fraudulent")] [Description("Fraudulent")]
    Fraudulent = 3,

    [EnumMember(Value = "SubscriptionCanceled")] [Description("SubscriptionCanceled")]
    SubscriptionCanceled = 4,

    [EnumMember(Value = "ServiceNotDelivered")] [Description("ServiceNotDelivered")]
    ServiceNotDelivered = 5
}
