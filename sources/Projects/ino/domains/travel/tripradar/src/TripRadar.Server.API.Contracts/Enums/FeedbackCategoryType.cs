using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FeedbackCategoryType
{
    [EnumMember(Value = "General")] [Description("General")]
    General = 1,

    [EnumMember(Value = "BugReport")] [Description("BugReport")]
    BugReport = 2,

    [EnumMember(Value = "FeatureRequest")] [Description("FeatureRequest")]
    FeatureRequest = 3,

    [EnumMember(Value = "Performance")] [Description("Performance")]
    Performance = 4,

    [EnumMember(Value = "UserInterface")] [Description("UserInterface")]
    UserInterface = 5,

    [EnumMember(Value = "Documentation")] [Description("Documentation")]
    Documentation = 6,

    [EnumMember(Value = "SubscriptionCancellation")] [Description("SubscriptionCancellation")]
    SubscriptionCancellation = 7
}
