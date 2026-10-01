namespace DigitalBrain.Assistant;

internal sealed record InvokePackageRequest(string Operation, string Input, Guid? InvocationId = null);
