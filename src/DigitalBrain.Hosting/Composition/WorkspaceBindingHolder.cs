namespace DigitalBrain;

internal sealed class WorkspaceBindingHolder
{
    private WorkspaceBinding? binding;

    internal WorkspaceBinding Binding
        => binding ?? throw new InvalidOperationException(
            "A workspace service was resolved outside a bound behavior turn.");

    internal void Bind(ScopeKey scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Value);
        if (binding is not null)
        {
            throw new InvalidOperationException("A workspace service scope is already bound.");
        }

        binding = new WorkspaceBinding(scope.Value);
    }
}
