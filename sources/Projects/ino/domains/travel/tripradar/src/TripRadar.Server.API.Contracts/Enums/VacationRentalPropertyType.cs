using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VacationRentalPropertyType
{
    [EnumMember(Value = "Apartments")] [Description("Apartments")]
    Apartments = 1,
    
    [EnumMember(Value = "Bungalows")] [Description("Bungalows")]
    Bungalows = 2,
    
    [EnumMember(Value = "Cabins")] [Description("Cabins")]
    Cabins = 3,
    
    [EnumMember(Value = "Chalets")] [Description("Chalets")]
    Chalets = 4,
    
    [EnumMember(Value = "Cottages")] [Description("Cottages")]
    Cottages = 5,
    
    [EnumMember(Value = "Gites")] [Description("Gites")]
    Gites = 6,
    
    [EnumMember(Value = "HolidayVillages")] [Description("HolidayVillages")]
    HolidayVillages = 7,
    
    [EnumMember(Value = "Houses")] [Description("Houses")]
    Houses = 8,
    
    [EnumMember(Value = "Houseboats")] [Description("Houseboats")]
    Houseboats = 9,
    
    [EnumMember(Value = "Villas")] [Description("Villas")]
    Villas = 10,
    
    [EnumMember(Value = "Other")] [Description("Other")]
    Other = 11,
    
    [EnumMember(Value = "ApartmentHotels")] [Description("ApartmentHotels")]
    ApartmentHotels = 21
}
