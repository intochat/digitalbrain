namespace DigitalBrain.Kernel;

// Builds an IChatClient for a caller-supplied provider/key (the LLM config a user entered for a pack),
// distinct from the single global kernel IChatClient. Returns null when the input is insufficient
// (e.g. openai with no key) so callers can fall back to the global IChatClient gracefully.
// Lives in Kernel.Abstractions (not Core) so peer integrations (Ino/Google/Salesforce) can reference it
// without depending on the full DigitalBrain.Kernel runtime/host project, and without Core taking on a
// Microsoft.Extensions.AI package reference (Core's boundary forbids runtime/host/integration packages).
public interface IScopedChatClientFactory
{
    Microsoft.Extensions.AI.IChatClient? Create(string provider, string? apiKey);
}
