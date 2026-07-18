using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace TripRadar.Server.API.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FlightSortByType
{
    [EnumMember(Value = "TopFlights")] [Description("TopFlights")]
    TopFlights = 1,
    
    [EnumMember(Value = "Price")] [Description("Price")]
    Price = 2,
    
    [EnumMember(Value = "DepartureTime")] [Description("DepartureTime")]
    DepartureTime = 3,
    
    [EnumMember(Value = "ArrivalTime")] [Description("ArrivalTime")]
    ArrivalTime = 4,
    
    [EnumMember(Value = "Duration")] [Description("Duration")]
    Duration = 5,
    
    [EnumMember(Value = "Emissions")] [Description("Emissions")]
    Emissions = 6
}
