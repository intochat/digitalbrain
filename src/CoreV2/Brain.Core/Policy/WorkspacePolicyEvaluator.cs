using Brain.Abstractions.Context;
using Brain.Abstractions.Operations;
using Brain.Abstractions.Policy;
using Brain.Core.Modules;

namespace Brain.Core.Policy;

public sealed class WorkspacePolicyEvaluator(ModuleSet modules) : IWorkspacePolicyEvaluator
{
    private readonly ModuleSet _modules = modules ?? throw new ArgumentNullException(nameof(modules));

    public PolicyDecision AuthorizeOperation(WorkspaceContext caller, OperationDescriptor operation)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(operation);

        var installedOperation = _modules.Modules
            .SelectMany(static module => module.Operations)
            .SingleOrDefault(candidate => candidate.Id == operation.Id);
        if (installedOperation is null || installedOperation != operation)
        {
            return PolicyDecision.Refused;
        }

        var entryRole = _modules.Modules
            .Where(module => module.Id == operation.Owner)
            .SelectMany(static module => module.Roles)
            .SingleOrDefault(role => role.Id == operation.EntryRole && role.Owner == operation.Owner);
        return entryRole is null ? PolicyDecision.Refused : PolicyDecision.Allowed;
    }

    public PolicyDecision AuthorizeGraphChange(ActivityContext context, GraphChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);

        var requestingModule = _modules.Modules.SingleOrDefault(module => module.Id == request.RequestedBy);
        if (requestingModule is null)
        {
            return PolicyDecision.Refused;
        }

        var targetRole = _modules.Modules
            .SelectMany(static module => module.Roles)
            .SingleOrDefault(role => role.Id == request.TargetRole);
        return targetRole is null ? PolicyDecision.Refused : PolicyDecision.Allowed;
    }
}
