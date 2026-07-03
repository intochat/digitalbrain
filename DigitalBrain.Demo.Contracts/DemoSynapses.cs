namespace DigitalBrain.Core;

[GenerateSerializer]
public record DemoMessageSynapse(string Text) : Synapse(nameof(DemoMessageSynapse), DateTimeOffset.UtcNow);

public interface IDemoNeuron : INeuron
{
    Task<string> GetLastMessageAsync();
}
