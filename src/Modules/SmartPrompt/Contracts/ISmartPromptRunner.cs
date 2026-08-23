using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.SmartPrompt;

[Alias("smart-prompt-runner")]
public partial interface ISmartPromptRunner :
    INeuron,
    IHandle<RunSmartPrompt>
{
    [Alias(nameof(ScheduleSmartPrompt))]
    Task ScheduleSmartPrompt(TimeSpan period);
}
