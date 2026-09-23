namespace DigitalBrain.Discovery;

public enum CapabilityCollection
{
    SystemCapabilities = 0,
    WorkspaceInstances = 1,
    UserMemories = 2,
}

// Each durable collection is named for the embedding model that produced its vectors.
public static class DiscoveryCollections
{
    public static string NameFor(CapabilityCollection collection, string embeddingModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        return collection switch
        {
            CapabilityCollection.SystemCapabilities => "intochat_capabilities__" + Sanitize(embeddingModel),
            CapabilityCollection.WorkspaceInstances => "intochat_workspace_instances__" + Sanitize(embeddingModel),
            CapabilityCollection.UserMemories => "intochat_user_memories__" + Sanitize(embeddingModel),
            _ => throw new ArgumentOutOfRangeException(nameof(collection), collection, "Unknown capability collection."),
        };
    }

    private static string Sanitize(string embeddingModel)
    {
        var characters = embeddingModel.Trim().ToLowerInvariant()
            .Select(static character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray();
        return new string(characters);
    }
}
