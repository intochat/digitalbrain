using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UsageEventSourceType
{
    [EnumMember(Value = "Api")] [Description("Api")]
    Api = 1,

    [EnumMember(Value = "Scheduled")] [Description("Scheduled")]
    Scheduled = 2,

    [EnumMember(Value = "Telegram")] [Description("Telegram")]
    Telegram = 3,

    [EnumMember(Value = "AI")] [Description("AI")]
    Ai = 4
}
