using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.SmartPrompt;

[GrainType("smartprompt")]
internal sealed class SmartPromptEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SmartPromptState> state)
    : Entity<SmartPromptState>(state), ISmartPrompt
{
    public async Task Save(SmartPromptDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.BodyText);
        ArgumentNullException.ThrowIfNull(document.Bindings);

        var revisionId = Guid.CreateVersion7();
        await SaveAsync(new SmartPromptState(document, revisionId));
    }

    public async Task Enable()
    {
        var current = State
            ?? throw new NeuronAuthorizationException($"Smart prompt '{this.GetPrimaryKeyString()}' has no saved document to enable.");

        if (current.Document.Enabled)
        {
            return;
        }

        await SaveAsync(current with { Document = current.Document with { Enabled = true } });
    }

    public async Task Disable()
    {
        var current = State
            ?? throw new NeuronAuthorizationException($"Smart prompt '{this.GetPrimaryKeyString()}' has no saved document to disable.");

        if (!current.Document.Enabled)
        {
            return;
        }

        await SaveAsync(current with { Document = current.Document with { Enabled = false } });
    }
}
