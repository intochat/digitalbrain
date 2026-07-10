using DigitalBrain.Core.V2;
using V2RequestContext = DigitalBrain.Core.V2.RequestContext;

namespace DigitalBrain.Infrastructure.Connectors.V2;

public sealed class V2ProviderOAuthAdapterRegistry(IEnumerable<IProviderOAuthAdapter> adapters) : IProviderOAuthAdapterRegistry
{
    private readonly IReadOnlyDictionary<string, IProviderOAuthAdapter> _adapters = adapters.ToDictionary(x => x.ProviderId, StringComparer.OrdinalIgnoreCase);
    public IProviderOAuthAdapter GetRequired(string provider) => _adapters.TryGetValue(provider, out var adapter) ? adapter : throw new KeyNotFoundException($"V2 provider '{provider}' is unavailable.");
}

public sealed class V2ConnectorAuthorizationPolicy(IProviderOAuthAdapterRegistry registry) : IConnectorAuthorizationPolicy
{
    public void DemandAuthorize(V2RequestContext context, string provider, IReadOnlyList<string> capabilityIds)
    {
        if (context.Assurance == AuthAssurance.None) throw new UnauthorizedAccessException("Authenticated assurance is required.");
        var adapter = registry.GetRequired(provider);
        foreach (var capabilityId in capabilityIds)
        {
            var capability = adapter.Capabilities.SingleOrDefault(x => x.Id == capabilityId) ?? throw new UnauthorizedAccessException("Connector capability is unavailable.");
            if (!context.Grants.Contains(capability.Id) && !context.Grants.Contains("connector:" + provider)) throw new UnauthorizedAccessException("Connector capability is not granted.");
        }
    }

    public void DemandUse(V2RequestContext context, CredentialRecord credential, ConnectorCapabilityDescriptor capability)
    {
        if (credential.Owner.TenantId != context.TenantId || credential.Owner.WorkspaceId != context.WorkspaceId || credential.Owner.Principal != context.Principal)
            throw new UnauthorizedAccessException("Credential is outside the authenticated V2 scope.");
        if (credential.Status != CredentialStatus.Connected) throw new InvalidOperationException("Credential is not connected.");
        if (capability.RequiredScopes.Any(scope => !credential.GrantedScopes.Contains(scope, StringComparer.Ordinal))) throw new UnauthorizedAccessException("Connector grant is insufficient.");
        if (capability.RequiresApproval && !context.Grants.Contains("brain.approve")) throw new UnauthorizedAccessException("Approval is required for this connector capability.");
    }
}
