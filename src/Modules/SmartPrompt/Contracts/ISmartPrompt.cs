using DigitalBrain.Abstractions.Entities;

namespace DigitalBrain.SmartPrompt;

[Alias("smart-prompt")]
public interface ISmartPrompt : IEntity<SmartPromptState>
{
    [Alias(nameof(Save))]
    Task Save(SmartPromptDocument document);

    [Alias(nameof(Enable))]
    Task Enable();

    [Alias(nameof(Disable))]
    Task Disable();
}
