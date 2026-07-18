using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FlightType
{
    [EnumMember(Value = "RoundTrip")] [Description("RoundTrip")]
    RoundTrip = 1,
    
    [EnumMember(Value = "OneWay")] [Description("OneWay")]
    OneWay = 2,
    
    [EnumMember(Value = "MultiCity")] [Description("MultiCity")]
    MultiCity = 3
}
