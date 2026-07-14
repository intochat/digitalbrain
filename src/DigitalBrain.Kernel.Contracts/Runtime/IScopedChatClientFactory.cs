namespace DigitalBrain.Kernel;

public interface IScopedChatClientFactory
{
    Microsoft.Extensions.AI.IChatClient? Create(string provider, string? apiKey);
}
