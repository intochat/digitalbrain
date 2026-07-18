using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserTierType
{
    [EnumMember(Value = "Basic")] [Description("Basic")]
    Basic = 1,

    [EnumMember(Value = "Essential")] [Description("Essential")]
    Essential = 2,

    [EnumMember(Value = "Advanced")] [Description("Advanced")]
    Advanced = 3
}
