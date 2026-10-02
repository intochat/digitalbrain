namespace DigitalBrain.Apps;

internal sealed record InvokeAppRequest(string Operation, string Input, Guid? InvocationId = null);
