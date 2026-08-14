using System.Collections.Immutable;
using Brain.Abstractions.Context;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Operations;
using Brain.Abstractions.Policy;
using Brain.Product.Abstractions.Authority;

namespace DigitalBrain.ProductHost.Catalog;

public interface IWorkspacePolicyVersionProvider
{
    bool TryGetCurrentVersion(WorkspaceId workspace, out int policyVersion);
}

public sealed class ProductOperationAccessPolicy
{
    public ProductOperationAccessPolicy(
        IEnumerable<string> requiredRoles,
        IEnumerable<string> requiredGrants)
    {
        RequiredRoles = Copy(requiredRoles, nameof(requiredRoles));
        RequiredGrants = Copy(requiredGrants, nameof(requiredGrants));
    }

    public ImmutableArray<string> RequiredRoles { get; }

    public ImmutableArray<string> RequiredGrants { get; }

    internal bool Allows(BrainAccessGrant grant)
        => RequiredRoles.All(required => grant.Roles.Contains(required, StringComparer.Ordinal))
            && RequiredGrants.All(required => grant.Grants.Contains(required, StringComparer.Ordinal));

    private static ImmutableArray<string> Copy(IEnumerable<string> claims, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(claims, parameterName);
        var copy = claims.ToImmutableArray();
        if (copy.Any(string.IsNullOrWhiteSpace))
        {
            throw new ProductOperationCatalogConfigurationException(
                $"Operation access policy '{parameterName}' cannot contain an empty claim.");
        }

        if (copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
        {
            throw new ProductOperationCatalogConfigurationException(
                $"Operation access policy '{parameterName}' cannot contain duplicate claims.");
        }

        return copy.Sort(StringComparer.Ordinal);
    }
}

public sealed class ProductOperationPolicyFilter
{
    private readonly IWorkspacePolicyEvaluator _policyEvaluator;
    private readonly IWorkspacePolicyVersionProvider _policyVersions;
    private readonly TimeProvider _timeProvider;

    public ProductOperationPolicyFilter(
        IWorkspacePolicyEvaluator policyEvaluator,
        IWorkspacePolicyVersionProvider policyVersions,
        TimeProvider timeProvider)
    {
        _policyEvaluator = policyEvaluator ?? throw new ArgumentNullException(nameof(policyEvaluator));
        _policyVersions = policyVersions ?? throw new ArgumentNullException(nameof(policyVersions));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public bool IsAvailable(BrainAccessGrant grant, ProductOperationRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(registration);

        var now = _timeProvider.GetUtcNow();
        if (now < grant.IssuedAt || now >= grant.ExpiresAt)
        {
            return false;
        }

        if (!_policyVersions.TryGetCurrentVersion(grant.Workspace, out var currentVersion)
            || currentVersion != grant.PolicyVersion)
        {
            return false;
        }

        if (!registration.AccessPolicy.Allows(grant))
        {
            return false;
        }

        var caller = new WorkspaceContext(grant.Workspace, grant.Principal, isServicePrincipal: false);
        return _policyEvaluator.AuthorizeOperation(caller, registration.DeclaredOperation)
            == PolicyDecision.Allowed;
    }
}
