using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HotelSortByType
{
    [EnumMember(Value = "LowestPrice")] [Description("LowestPrice")]
    LowestPrice = 3,
    
    [EnumMember(Value = "HighestRating")] [Description("HighestRating")]
    HighestRating = 8,
    
    [EnumMember(Value = "MostReviewed")] [Description("MostReviewed")]
    MostReviewed = 13
}
