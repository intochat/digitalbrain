using System.Text.Json.Serialization;

namespace TripRadar.Server.Application.DTO.Models;

public class ServiceOptionsDTO
{
    [JsonPropertyName("dine_in")]
    public bool? DineIn { get; set; }

    [JsonPropertyName("takeout")]
    public bool? Takeout { get; set; }

    [JsonPropertyName("delivery")]
    public bool? Delivery { get; set; }

    [JsonPropertyName("no_delivery")]
    public bool? NoDelivery { get; set; }

    [JsonPropertyName("in_store_pickup")]
    public bool? InStorePickup { get; set; }

    [JsonPropertyName("in_store_shopping")]
    public bool? InStoreShopping { get; set; }

    [JsonPropertyName("curbside_pickup")]
    public bool? CurbsidePickup { get; set; }

    [JsonPropertyName("no_contact_delivery")]
    public bool? NoContactDelivery { get; set; }

    [JsonPropertyName("reservable")]
    public bool? Reservable { get; set; }

    [JsonPropertyName("wheelchair_accessible")]
    public bool? WheelchairAccessible { get; set; }
}
