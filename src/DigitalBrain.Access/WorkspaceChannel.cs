namespace DigitalBrain;

public interface WorkspaceChannel
{
    SynapsePublisher Publisher { get; }

    JournalReader Journal { get; }
}
