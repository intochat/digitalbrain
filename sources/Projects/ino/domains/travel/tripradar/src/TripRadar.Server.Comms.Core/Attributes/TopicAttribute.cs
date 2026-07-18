namespace TripRadar.Server.Comms.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class TopicAttribute : Attribute
{
    public TopicAttribute(string topicName)
    {
        if (string.IsNullOrWhiteSpace(topicName))
        {
            throw new ArgumentException("Topic name cannot be null or empty", nameof(topicName));
        }

        TopicName = topicName;
    }

    public string TopicName { get; }
}
