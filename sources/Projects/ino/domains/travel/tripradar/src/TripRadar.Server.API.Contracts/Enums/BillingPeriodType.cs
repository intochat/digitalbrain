using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BillingPeriodType
{
    [EnumMember(Value = "Monthly")] [Description("Monthly")]
    Monthly = 1,

    [EnumMember(Value = "Yearly")] [Description("Yearly")]
    Yearly = 2
}
