using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HotelsPropertyType
{
    [EnumMember(Value = "BeachHotels")] [Description("BeachHotels")]
    BeachHotels = 12,
    
    [EnumMember(Value = "BoutiqueHotels")] [Description("BoutiqueHotels")]
    BoutiqueHotels = 13,
    
    [EnumMember(Value = "Hostels")] [Description("Hostels")]
    Hostels = 14,
    
    [EnumMember(Value = "Inns")] [Description("Inns")]
    Inns = 15,
    
    [EnumMember(Value = "Motels")] [Description("Motels")]
    Motels = 16,
    
    [EnumMember(Value = "Resorts")] [Description("Resorts")]
    Resorts = 17,
    
    [EnumMember(Value = "SpaHotels")] [Description("SpaHotels")]
    SpaHotels = 18,
    
    [EnumMember(Value = "BedAndBreakfasts")] [Description("BedAndBreakfasts")]
    BedAndBreakfasts = 19,
    
    [EnumMember(Value = "Other")] [Description("Other")]
    Other = 20,
    
    [EnumMember(Value = "ApartmentHotels")] [Description("ApartmentHotels")]
    ApartmentHotels = 21,
    
    [EnumMember(Value = "Minshuku")] [Description("Minshuku")]
    Minshuku = 22,
    
    [EnumMember(Value = "JapaneseStyleBusinessHotels")] [Description("JapaneseStyleBusinessHotels")]
    JapaneseStyleBusinessHotels = 23,
    
    [EnumMember(Value = "Ryokan")] [Description("Ryokan")]
    Ryokan = 24
}
