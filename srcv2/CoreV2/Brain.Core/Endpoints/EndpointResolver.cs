using Brain.Abstractions.Context;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Core.Modules;

namespace Brain.Core.Endpoints;

// This value is an internal runtime address. It intentionally has no counterpart in
// Brain.Abstractions operation contracts or activity projections.
internal sealed record EndpointAddress(
    WorkspaceId Workspace,
    ModuleId Module,
    NeuronRoleId Role,
    string ScopeToken);

internal interface IEndpointResolver
{
    EndpointAddress Resolve(NeuronRoleDescriptor role, WorkspaceContext context);
}

internal sealed class EndpointResolver(ModuleSet modules) : IEndpointResolver
{
    private const string WorkspaceScopeToken = "workspace";

    private readonly ModuleSet _modules = modules ?? throw new ArgumentNullException(nameof(modules));

    public EndpointAddress Resolve(NeuronRoleDescriptor role, WorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(context);

        var installedRole = _modules.Modules
            .SelectMany(static module => module.Roles)
            .SingleOrDefault(candidate => candidate.Id == role.Id
                && candidate.Owner == role.Owner
                && candidate.Scope == role.Scope);
        if (installedRole is null)
        {
            throw new InvalidOperationException(
                $"Role '{role.Id}' with scope '{role.Scope}' is not declared by module '{role.Owner}'.");
        }

        var scopeToken = installedRole.Scope == NeuronScope.Workspace
            ? WorkspaceScopeToken
            : context.Principal.Value;
        return new EndpointAddress(context.Workspace, installedRole.Owner, installedRole.Id, scopeToken);
    }
}
