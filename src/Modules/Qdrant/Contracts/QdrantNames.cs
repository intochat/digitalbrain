namespace DigitalBrain.Qdrant;

// Grain type and the Aspire resource names the hosting projection creates.
public static class QdrantNames
{
    public const string NeuronType = "qdrant";
    public const string DefaultNeuron = "default";

    public const string Server = "Qdrant";
    public const string CollectionName = "digitalbrain";

    // Width of the app's default embedding, text-embedding-3-small.
    public const int DefaultVectorSize = 1536;
}