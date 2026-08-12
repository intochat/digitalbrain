using System.Collections.Concurrent;

namespace DigitalBrain.Execution;

public interface IWorkerTypeRegistration
{
    string GrainType { get; }
}

