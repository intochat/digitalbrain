using System.Collections.Immutable;
using Brain.Abstractions.Identity;

namespace Brain.Abstractions.Context;

public sealed record WorkspaceContext
{
    public WorkspaceContext(WorkspaceId workspace, PrincipalId principal, bool isServicePrincipal)
    {
        if (workspace.IsEmpty)
        {
            throw new ArgumentException("A workspace context requires a workspace.", nameof(workspace));
        }

        if (string.IsNullOrWhiteSpace(principal.Value))
        {
            throw new ArgumentException("A workspace context requires a principal.", nameof(principal));
        }

        Workspace = workspace;
        Principal = principal;
        IsServicePrincipal = isServicePrincipal;
    }

    public WorkspaceId Workspace { get; }

    public PrincipalId Principal { get; }

    public bool IsServicePrincipal { get; }
}

public sealed record ActivityContext
{
    public ActivityContext(
        WorkspaceId workspace,
        PrincipalId principal,
        BrainActivityId activity,
        CorrelationId correlation,
        Delegation delegation)
    {
        RequireWorkspace(workspace, nameof(workspace));
        RequirePrincipal(principal, nameof(principal));
        if (activity.Value == Guid.Empty)
        {
            throw new ArgumentException("An activity context requires an activity id.", nameof(activity));
        }

        if (string.IsNullOrWhiteSpace(correlation.Value))
        {
            throw new ArgumentException("An activity context requires a correlation id.", nameof(correlation));
        }

        ArgumentNullException.ThrowIfNull(delegation);
        Workspace = workspace;
        Principal = principal;
        Activity = activity;
        Correlation = correlation;
        Delegation = delegation;
    }

    public ActivityContext(
        WorkspaceId workspace,
        PrincipalId principal,
        BrainActivityId activity,
        CorrelationId correlation)
        : this(workspace, principal, activity, correlation, Delegation.Empty)
    {
    }

    public WorkspaceId Workspace { get; }

    public PrincipalId Principal { get; }

    public BrainActivityId Activity { get; }

    public CorrelationId Correlation { get; }

    public Delegation Delegation { get; }

    private static void RequireWorkspace(WorkspaceId workspace, string parameterName)
    {
        if (workspace.IsEmpty)
        {
            throw new ArgumentException("A context requires a workspace.", parameterName);
        }
    }

    private static void RequirePrincipal(PrincipalId principal, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(principal.Value))
        {
            throw new ArgumentException("A context requires a principal.", parameterName);
        }
    }
}

public sealed record Delegation
{
    public static Delegation Empty { get; } = new([], []);

    public Delegation(
        IEnumerable<OperationId> operations,
        IEnumerable<CapabilityId> capabilities)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(capabilities);
        Operations = operations.ToImmutableHashSet();
        Capabilities = capabilities.ToImmutableHashSet();
    }

    public ImmutableHashSet<OperationId> Operations { get; }

    public ImmutableHashSet<CapabilityId> Capabilities { get; }

    public Delegation Intersect(Delegation other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new Delegation(
            Operations.Intersect(other.Operations),
            Capabilities.Intersect(other.Capabilities));
    }
}
