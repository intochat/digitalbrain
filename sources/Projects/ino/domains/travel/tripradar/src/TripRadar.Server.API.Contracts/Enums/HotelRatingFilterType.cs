using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HotelRatingFilterType
{
    [EnumMember(Value = "3.5+")] [Description("3.5+")]
    Rating35Plus = 7,

    [EnumMember(Value = "4.0+")] [Description("4.0+")]
    Rating40Plus = 8,

    [EnumMember(Value = "4.5+")] [Description("4.5+")]
    Rating45Plus = 9
}
