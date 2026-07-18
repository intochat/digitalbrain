using System.Text.Json.Serialization;

namespace TripRadar.Server.Comms.Core.Events;

public class PublishableEvent
{
    [JsonPropertyName("eventId")]
    public Guid EventId { get; set; }

    [JsonPropertyName("eventDate")]
    public DateTime EventDate { get; set; }

    [JsonPropertyName("eventOwner")]
    public Owner EventOwner { get; set; } = null!;

    [JsonPropertyName("eventData")]
    public object EventData { get; set; } = null!;
}

public class Owner
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = null!;
}
