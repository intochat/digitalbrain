using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StopsType
{
    [EnumMember(Value = "Any")] [Description("Any")]
    Any = 0,

    [EnumMember(Value = "Nonstop")] [Description("Nonstop")]
    Nonstop = 1,

    [EnumMember(Value = "OneStopOrFewer")] [Description("OneStopOrFewer")]
    OneStopOrFewer = 2,

    [EnumMember(Value = "TwoStopsOrFewer")] [Description("TwoStopsOrFewer")]
    TwoStopsOrFewer = 3
}
