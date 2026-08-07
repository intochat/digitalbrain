namespace DigitalBrain;

internal sealed record WorkspaceServiceRegistration(
    Type ServiceType,
    Func<WorkspaceBinding, object> Factory);
