using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

[ClientEntryPoint]
[Alias("ai.voice-to-text")]
public partial interface IVoiceToText :
    INeuron,
    IHandle<TranscribeAudio>
{
    const string GrainTypeName = "voicetotext";
    const string InstanceName = "main";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);
}
