using DigitalBrain.Capabilities;

namespace DigitalBrain.Tests.Unit;

// 09-03 (T-09-08): privileged grants and secret declarations come from the manifest and fail closed.
public sealed class ModuleDeclarationsFacts
{
    private static ModuleManifest Manifest(string id, IReadOnlyList<PrivilegedGrant> privileged, params string[] secrets)
        => new ModuleManifest([], [], [], [], [.. secrets.Select(Secret.Named)]) { Id = id, Privileged = privileged };

    [Fact]
    public void Privileged_is_empty_by_default()
    {
        Assert.Empty(new ModuleManifest([], [], [], [], []).Privileged);
    }

    [Fact]
    public void Granted_module_is_privileged_for_its_member_only()
    {
        var declarations = new ModuleDeclarations([Manifest("auth", [new(Capability.Identity.V1, "Issue")])]);
        Assert.True(declarations.IsPrivileged("auth", Capability.Identity.V1, "Issue"));
        Assert.False(declarations.IsPrivileged("auth", Capability.Identity.V1, "End"));
        Assert.False(declarations.IsPrivileged("auth", Capability.Secrets.V1, "Issue"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("notes")]
    [InlineData("unknown")]
    public void Every_other_caller_is_refused(string? moduleId)
    {
        var declarations = new ModuleDeclarations([Manifest("auth", [new(Capability.Identity.V1, "Issue")]), Manifest("notes", [])]);
        Assert.False(declarations.IsPrivileged(moduleId, Capability.Identity.V1, "Issue"));
        var denied = Assert.Throws<PrivilegeDenied>(() => declarations.RequirePrivileged(moduleId, Capability.Identity.V1, "Issue"));
        Assert.IsAssignableFrom<UnauthorizedAccessException>(denied);
        Assert.Equal("Issue", denied.Member);
        Assert.Equal(Capability.Identity.V1, denied.Capability);
    }

    [Fact]
    public void None_refuses_everything()
    {
        Assert.False(ModuleDeclarations.None.IsPrivileged("auth", Capability.Identity.V1, "Issue"));
        Assert.False(ModuleDeclarations.None.DeclaresSecret("auth", "signin-issuer"));
    }

    [Theory]
    [InlineData("signin-issuer", true)]
    [InlineData("signin-issuer:SigningKey", true)]
    [InlineData("acs-email:ConnectionString", false)]
    [InlineData("signin", false)]
    [InlineData(":SigningKey", false)]
    public void Declared_secret_covers_its_keys(string name, bool declared)
    {
        var declarations = new ModuleDeclarations([Manifest("auth", [], "signin-issuer")]);
        Assert.Equal(declared, declarations.DeclaresSecret("auth", name));
        Assert.False(declarations.DeclaresSecret("notes", name));
        Assert.False(declarations.DeclaresSecret(null, name));
    }
}
