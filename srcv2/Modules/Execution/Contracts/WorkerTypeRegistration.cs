namespace DigitalBrain.Execution;

public sealed record WorkerTypeRegistration(string GrainType) : IWorkerTypeRegistration;