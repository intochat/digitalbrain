using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VacationRentalAmenityType
{
    [EnumMember(Value = "HotTub")] [Description("HotTub")]
    HotTub = 2,
    
    [EnumMember(Value = "AirConditioned")] [Description("AirConditioned")]
    AirConditioned = 4,
    
    [EnumMember(Value = "OutdoorGrill")] [Description("OutdoorGrill")]
    OutdoorGrill = 6,
    
    [EnumMember(Value = "Fireplace")] [Description("Fireplace")]
    Fireplace = 10,
    
    [EnumMember(Value = "PatioOrDeck")] [Description("PatioOrDeck")]
    PatioOrDeck = 12,
    
    [EnumMember(Value = "Kitchen")] [Description("Kitchen")]
    Kitchen = 15,
    
    [EnumMember(Value = "FitnessCentre")] [Description("FitnessCentre")]
    FitnessCentre = 16,
    
    [EnumMember(Value = "Cot")] [Description("Cot")]
    Cot = 18,
    
    [EnumMember(Value = "BeachAccess")] [Description("BeachAccess")]
    BeachAccess = 20,
    
    [EnumMember(Value = "ChildFriendly")] [Description("ChildFriendly")]
    ChildFriendly = 21,
    
    [EnumMember(Value = "PetFriendly")] [Description("PetFriendly")]
    PetFriendly = 24,
    
    [EnumMember(Value = "FreeWiFi")] [Description("FreeWiFi")]
    FreeWiFi = 29,
    
    [EnumMember(Value = "Pool")] [Description("Pool")]
    Pool = 32
}
