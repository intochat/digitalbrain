using System.ComponentModel;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiscountType
{
    [Description("Percentage")]
    Percentage = 1,

    [Description("FixedAmount")]
    FixedAmount = 2
}
