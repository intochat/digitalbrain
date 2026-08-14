using System.Reflection;
using Brain.Abstractions.Context;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Operations;
using Brain.Core.Endpoints;
using Brain.Core.Modules;
using Xunit;

namespace Brain.Core.Tests;

public sealed class EndpointResolverTests
{
    [Fact]
    public void PrincipalScopedRoleDerivesDistinctEndpointsForTwoPrincipals()
    {
        var role = new NeuronRoleDescriptor(
            new NeuronRoleId("proof.entry"),
            NeuronScope.Principal,
            new ModuleId("proof"));
        var resolver = new EndpointResolver(ModuleSetFor(role));

        var alice = resolver.Resolve(role, Context("workspace/sales", "principal/alice"));
        var bob = resolver.Resolve(role, Context("workspace/sales", "principal/bob"));

        Assert.NotEqual(alice, bob);
        Assert.Equal(alice.Role, bob.Role);
    }

    [Fact]
    public void WorkspaceScopedRoleUsesStableWorkspaceScopeToken()
    {
        var role = new NeuronRoleDescriptor(
            new NeuronRoleId("proof.workspace-entry"),
            NeuronScope.Workspace,
            new ModuleId("proof"));
        var resolver = new EndpointResolver(ModuleSetFor(role));

        var alice = resolver.Resolve(role, Context("workspace/sales", "principal/alice"));
        var bob = resolver.Resolve(role, Context("workspace/sales", "principal/bob"));

        Assert.Equal(alice, bob);
        Assert.Equal("workspace", alice.ScopeToken);
    }

    [Fact]
    public void ResolverRefusesARoleThatIsNotDeclaredByTheInstalledOwner()
    {
        var installed = new NeuronRoleDescriptor(
            new NeuronRoleId("proof.entry"),
            NeuronScope.Workspace,
            new ModuleId("proof"));
        var attempted = new NeuronRoleDescriptor(
            new NeuronRoleId("proof.entry"),
            NeuronScope.Principal,
            new ModuleId("proof"));
        var resolver = new EndpointResolver(ModuleSetFor(installed));

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(attempted, Context("workspace/sales", "principal/alice")));
    }

    [Fact]
    public void OperationInvocationCannotAcceptAnEndpointAddress()
    {
        var invocationProperties = typeof(OperationInvocation<EndpointInput>)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);
        var constructorParameters = typeof(OperationInvocation<EndpointInput>)
            .GetConstructors()
            .SelectMany(static constructor => constructor.GetParameters());

        Assert.DoesNotContain(invocationProperties, static property => property.PropertyType == typeof(EndpointAddress));
        Assert.DoesNotContain(constructorParameters, static parameter => parameter.ParameterType == typeof(EndpointAddress));
        Assert.False(typeof(EndpointAddress).IsPublic);
    }

    private static WorkspaceContext Context(string workspace, string principal)
        => new(new WorkspaceId(workspace), new PrincipalId(principal), isServicePrincipal: false);

    private static ModuleSet ModuleSetFor(NeuronRoleDescriptor role)
        => ManifestValidator.Validate(
        [
            new ModuleManifest(
                role.Owner,
                new ModuleVersion(1, 0, 0),
                [],
                [role],
                [],
                [],
                [],
                [],
                [],
                []),
        ]);

    private sealed record EndpointInput;
}
