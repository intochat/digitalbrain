using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HotelAmenityType
{
    [EnumMember(Value = "FreeParking")] [Description("FreeParking")]
    FreeParking = 1,
    
    [EnumMember(Value = "Parking")] [Description("Parking")]
    Parking = 3,
    
    [EnumMember(Value = "IndoorPool")] [Description("IndoorPool")]
    IndoorPool = 4,
    
    [EnumMember(Value = "OutdoorPool")] [Description("OutdoorPool")]
    OutdoorPool = 5,
    
    [EnumMember(Value = "Pool")] [Description("Pool")]
    Pool = 6,
    
    [EnumMember(Value = "FitnessCenter")] [Description("FitnessCenter")]
    FitnessCenter = 7,
    
    [EnumMember(Value = "Restaurant")] [Description("Restaurant")]
    Restaurant = 8,
    
    [EnumMember(Value = "FreeBreakfast")] [Description("FreeBreakfast")]
    FreeBreakfast = 9,
    
    [EnumMember(Value = "Spa")] [Description("Spa")]
    Spa = 10,
    
    [EnumMember(Value = "BeachAccess")] [Description("BeachAccess")]
    BeachAccess = 11,
    
    [EnumMember(Value = "ChildFriendly")] [Description("ChildFriendly")]
    ChildFriendly = 12,
    
    [EnumMember(Value = "Bar")] [Description("Bar")]
    Bar = 15,
    
    [EnumMember(Value = "PetFriendly")] [Description("PetFriendly")]
    PetFriendly = 19,
    
    [EnumMember(Value = "RoomService")] [Description("RoomService")]
    RoomService = 22,
    
    [EnumMember(Value = "FreeWiFi")] [Description("FreeWiFi")]
    FreeWiFi = 35,
    
    [EnumMember(Value = "AirConditioned")] [Description("AirConditioned")]
    AirConditioned = 40,
    
    [EnumMember(Value = "AllInclusiveAvailable")] [Description("AllInclusiveAvailable")]
    AllInclusiveAvailable = 52,
    
    [EnumMember(Value = "WheelchairAccessible")] [Description("WheelchairAccessible")]
    WheelchairAccessible = 53,
    
    [EnumMember(Value = "EVCharger")] [Description("EVCharger")]
    EVCharger = 61
}
