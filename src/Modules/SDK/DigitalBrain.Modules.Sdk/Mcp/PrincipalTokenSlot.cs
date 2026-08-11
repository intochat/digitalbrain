using Orleans.Journaling;

namespace DigitalBrain.Modules.Sdk.Mcp;

// One principal's token envelope inside a durable dictionary. The dictionary is
// the journaling participant; this type only addresses a single subject key.
public sealed class PrincipalTokenSlot(
    IDurableDictionary<string, byte[]> store,
    string subjectKey)
{
    public byte[]? Read()
        => store.TryGetValue(subjectKey, out var bytes) && bytes is { Length: > 0 }
            ? bytes
            : null;

    public void Write(byte[]? protectedPayload)
    {
        if (protectedPayload is null || protectedPayload.Length == 0)
        {
            store.Remove(subjectKey);
            return;
        }

        store[subjectKey] = protectedPayload;
    }
}
