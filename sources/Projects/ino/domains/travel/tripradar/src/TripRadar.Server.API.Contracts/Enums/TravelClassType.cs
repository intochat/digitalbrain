using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TravelClassType
{
    [EnumMember(Value = "Economy")] [Description("Economy")]
    Economy = 1,

    [EnumMember(Value = "PremiumEconomy")] [Description("PremiumEconomy")]
    PremiumEconomy = 2,

    [EnumMember(Value = "Business")] [Description("Business")]
    Business = 3,

    [EnumMember(Value = "First")] [Description("First")]
    First = 4
}
