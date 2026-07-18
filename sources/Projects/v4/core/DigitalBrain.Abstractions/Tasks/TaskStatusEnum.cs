using Orleans;

namespace DigitalBrain.Abstractions.Tasks;

[GenerateSerializer]
public enum TaskStatusEnum
{
    Created = 0,
    Pending = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Resumed = 5,
    Suspended = 6
}
