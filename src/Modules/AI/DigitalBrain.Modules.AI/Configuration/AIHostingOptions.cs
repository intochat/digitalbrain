using Microsoft.Extensions.Configuration;

namespace DigitalBrain.AI;

public sealed class AIHostingOptions
{
    public List<string> Llms { get; set; } = [];
    public List<string> Embeddings { get; set; } = [];
}
