using DigitalBrain.Capabilities;
using System.Reflection;
using System.Text.Json;
using DigitalBrain.Capabilities.Identity;

namespace DigitalBrain.Capabilities.Identity.Tests.Unit;

public sealed class IdentityCapabilityFacts
{
    [Theory]
    [InlineData(typeof(IIdentity))]
    [InlineData(typeof(IGrants))]
    public void Both_halves_are_identity_v1(Type contract)
    {
        var attribute = contract.GetCustomAttribute<CapabilityAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(Capability.Identity.V1, attribute.Id);
    }

    // Every IIdentityClient member of os 0.6.x has a home in identity@1 (D-02).
    [Fact]
    public void Covers_every_identity_client_member()
    {
        string[] client =
        [
            "ReadAccount", "OwnerOfBrain", "EnsureAccount", "CreateBrain", "EnsureBrain", "ProvisionOwner", "IsRegistered",
            "RegisterPrincipal", "CompleteRegistration", "BrainsOf", "KindOf", "BrainExists", "Membership", "EnsureMember",
            "RemoveMember", "RemovePrincipalMember", "Authorize", "ListGrants", "GrantAsOwner", "RevokeAsOwner",
        ];
        var names = typeof(IIdentity).GetMethods().Concat(typeof(IGrants).GetMethods()).Select(method => method.Name).ToHashSet(StringComparer.Ordinal);
        Assert.All(client, name => Assert.Contains(name, names));
        Assert.Equal(client.Length, names.Count);
    }

    [Fact]
    public void Grants_hold_only_grant_members()
    {
        Assert.Equal(["Authorize", "GrantAsOwner", "ListGrants", "RevokeAsOwner"], typeof(IGrants).GetMethods().Select(method => method.Name).Order(StringComparer.Ordinal));
    }

    // D-15: no CapabilityCaller (runtime caller) and no HttpContext (ISessionIssuer stays in os).
    [Fact]
    public void No_member_takes_a_caller_or_http_context()
    {
        var types = typeof(IIdentity).GetMethods().Concat(typeof(IGrants).GetMethods()).Concat(typeof(ISignInHandler).GetMethods())
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType));
        Assert.DoesNotContain(types, type => type.Name.Contains("Caller", StringComparison.Ordinal) || type.Name.Contains("HttpContext", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_member_takes_a_cancellation_token_last()
    {
        foreach (var method in typeof(IIdentity).GetMethods().Concat(typeof(IGrants).GetMethods()).Concat(typeof(ISignInHandler).GetMethods()))
        {
            Assert.Equal(typeof(CancellationToken), method.GetParameters()[^1].ParameterType);
        }
    }

    [Fact]
    public void Assembly_has_no_session_issuer_and_no_aspnet()
    {
        var assembly = typeof(IIdentity).Assembly;
        Assert.DoesNotContain(assembly.GetTypes(), type => type.Name == "ISessionIssuer");
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        var exposed = assembly.GetExportedTypes()
            .SelectMany(type => type.GetMethods().SelectMany(method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType)));
        Assert.DoesNotContain(exposed, type => type.Namespace?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true);
    }
}

public sealed class IdentityRecordFacts
{
    private static readonly Member Owner = new()
    {
        PrincipalId = "p-1",
        AccountId = "a-1",
        BrainId = "b-1",
        Role = MemberRole.Owner,
        DisplayName = "Ada",
    };

    [Fact]
    public void Records_round_trip_through_json()
    {
        var account = new Account { AccountId = "a-1", Name = "Ada", OwnerPrincipalId = "p-1", CreatedAt = DateTimeOffset.UnixEpoch };
        Assert.Equal(account, JsonSerializer.Deserialize<Account>(JsonSerializer.Serialize(account)));
        Assert.Equal(Owner, JsonSerializer.Deserialize<Member>(JsonSerializer.Serialize(Owner)));
        var provision = new BrainProvision(Owner, NewBrain: true);
        Assert.Equal(provision, JsonSerializer.Deserialize<BrainProvision>(JsonSerializer.Serialize(provision)));
        var entry = new BrainEntry("b-1", BrainKind.Circle);
        Assert.Equal(entry, JsonSerializer.Deserialize<BrainEntry>(JsonSerializer.Serialize(entry)));
    }

    [Fact]
    public void Enum_values_match_os()
    {
        Assert.Equal([0, 1, 2], Enum.GetValues<MemberRole>().Select(value => (int)value));
        Assert.Equal([0, 1], Enum.GetValues<BrainKind>().Select(value => (int)value));
        Assert.Equal([0, 1, 2], Enum.GetValues<GrantMode>().Select(value => (int)value));
        Assert.Equal(7, Enum.GetValues<CallDenial>().Length);
    }

    [Fact]
    public void Member_field_order_mirrors_os()
    {
        Assert.Equal(["PrincipalId", "AccountId", "BrainId", "Role", "DisplayName", "JoinedAt"], typeof(Member).GetProperties().Select(p => p.Name));
    }
}

public sealed class GrantFacts
{
    private static Grant Always(bool revoked = false) => new()
    {
        AppId = "notes",
        SemanticTypeId = "person.birthDate",
        Mode = GrantMode.Always,
        WorkspaceId = "b-1",
        Revoked = revoked,
    };

    [Fact]
    public void Grant_round_trips_and_defaults_unrevoked()
    {
        var grant = Always();
        Assert.False(grant.Revoked);
        Assert.Null(grant.ConversationId);
        Assert.Equal(grant, JsonSerializer.Deserialize<Grant>(JsonSerializer.Serialize(grant)));
    }

    [Theory]
    [InlineData(GrantMode.Always, null, "c-9", true)]
    [InlineData(GrantMode.Once, null, "c-9", true)]
    [InlineData(GrantMode.ThisChat, "c-9", "c-9", true)]
    [InlineData(GrantMode.ThisChat, "c-9", "c-1", false)]
    [InlineData(GrantMode.ThisChat, "c-9", null, false)]
    public void Covers_matches_app_type_workspace_and_chat(GrantMode mode, string? grantChat, string? callChat, bool expected)
    {
        var grant = Always() with { Mode = mode, ConversationId = grantChat };
        Assert.Equal(expected, grant.Covers("notes", "person.birthDate", "b-1", callChat));
    }

    [Fact]
    public void Revoked_or_mismatched_grant_covers_nothing()
    {
        Assert.False(Always(revoked: true).Covers("notes", "person.birthDate", "b-1", null));
        Assert.False(Always().Covers("other", "person.birthDate", "b-1", null));
        Assert.False(Always().Covers("notes", "person.name", "b-1", null));
        Assert.False(Always().Covers("notes", "person.birthDate", "b-2", null));
    }

    [Fact]
    public void Decisions_allow_and_deny()
    {
        Assert.True(CallDecision.Allow().Allowed);
        var denied = CallDecision.Deny(CallDenial.MissingGrant, "no grant for person.birthDate");
        Assert.False(denied.Allowed);
        Assert.Equal(CallDenial.MissingGrant, denied.Denial);
        Assert.Equal("no grant for person.birthDate", denied.Explanation);
    }

    [Fact]
    public void Call_request_carries_no_caller()
    {
        var request = new CallRequest { TargetNeuron = "notes", Operation = "Read" };
        Assert.Empty(request.SemanticTypeIds);
        Assert.DoesNotContain(typeof(CallRequest).GetProperties(), property => property.Name.Contains("Caller", StringComparison.Ordinal));
    }
}
