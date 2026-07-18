namespace TripRadar.Bot.Configuration;

public sealed class KafkaConsumerOptions
{
    public const string SectionName = "KafkaConsumer";

    public string[] Topics { get; set; } = ["Flights", "Hotels", "LocalPlaces", "Events"];

    public string GroupId { get; set; } = "bot";

    public int MaxRetries { get; set; } = 5;
}
