namespace DigitalBrain.Coding;

// [GenerateSerializer] so the workspace neuron can let this cross the grain boundary: without it,
// Orleans has no codec for the type and a query made through the neuron fails with an opaque
// "no codec" error instead of delivering status.Advice as the exception message. WorkspaceStatus
// itself carries no serializer, so nothing here is [Id]-tagged; the advice text is what survives
// the trip, via the base Exception message.
[GenerateSerializer]
[Alias("coding.workspace-not-ready")]
public sealed class WorkspaceNotReadyException(WorkspaceStatus status) : InvalidOperationException(status.Advice);