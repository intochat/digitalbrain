using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Capabilities.Identity;

namespace DigitalBrain.Capabilities.Identity.Tests.Unit;

// 09-03 (D-08): the auth-driven identity@1 members, outside the per-implementation kit.
public sealed class AuthAdditionsFacts
{
    [Theory]
    [InlineData(typeof(ISessions))]
    [InlineData(typeof(IIdentityProvider))]
    public void Added_contracts_are_identity_v1(Type contract)
    {
        Assert.Equal(Capability.Identity.V1, contract.GetCustomAttribute<CapabilityAttribute>()?.Id);
    }

    [Fact]
    public void Sessions_members_are_issue_end_validate()
    {
        Assert.Equal(["End", "Issue", "Validate"], typeof(ISessions).GetMethods().Select(method => method.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Account_surface_is_a_nullable_neuron_id_and_posture_is_open_or_secured()
    {
        Assert.Equal(typeof(NeuronId?), typeof(IdentityProviderDescriptor).GetProperty(nameof(IdentityProviderDescriptor.AccountSurface))!.PropertyType);
        Assert.Equal([0, 1], Enum.GetValues<IdentityPosture>().Select(value => (int)value));
        Assert.Null(new IdentityProviderDescriptor { ProviderId = "x" }.AccountSurface);
    }

    [Theory]
    [InlineData("principal")]
    [InlineData("account")]
    [InlineData("brain")]
    [InlineData("role")]
    [InlineData("ROLE")]
    public void Identity_owned_claims_are_rejected(string claim)
    {
        var request = new SessionRequest { Member = Member(), Claims = new Dictionary<string, string> { [claim] = "x" } };
        Assert.Throws<ArgumentException>(() => SessionClaims.Validate(request));
    }

    [Fact]
    public void Extra_claims_pass_and_owned_claims_come_from_the_member()
    {
        var member = Member();
        SessionClaims.Validate(new SessionRequest { Member = member, Claims = new Dictionary<string, string> { ["sid"] = "s", ["auth_time"] = "1", ["amr"] = "pwd" }, Lifetime = TimeSpan.FromMinutes(1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionClaims.Validate(new SessionRequest { Member = member, Lifetime = TimeSpan.FromSeconds(-1) }));
        var owned = SessionClaims.Of(member);
        Assert.Equal("p", owned[SessionClaims.Principal]);
        Assert.Equal("a", owned[SessionClaims.Account]);
        Assert.Equal("b", owned[SessionClaims.Brain]);
        Assert.Equal("Owner", owned[SessionClaims.Role]);
    }

    private static Member Member() => new() { PrincipalId = "p", AccountId = "a", BrainId = "b", Role = MemberRole.Owner, DisplayName = "P" };
}

// T-09-09: the external sign-in ticket keeps the os wire format, a 60 s lifetime, the audience check and the size cap.
public sealed class ExternalSignInTicketFacts
{
    private const string Audience = "https://test.intochat.io";
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static ExternalSignInClaims Claims(DateTimeOffset? expires = null) =>
        new("cloud", Audience, "Ada@Example.com", "Ada", true, "google", "jti-1", expires ?? Now.AddSeconds(30));

    private static FixedTime Clock(DateTimeOffset at) => new(at);

    [Fact]
    public void Signed_ticket_verifies_once_for_its_audience()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ticket = ExternalSignInTicket.Sign(Claims(), key, Clock(Now));
        var verified = ExternalSignInTicket.Verify(ticket, issuer => issuer == "cloud" ? (key, Audience) : null, Clock(Now));
        Assert.NotNull(verified);
        Assert.Equal("Ada@Example.com", verified.Email);
        Assert.Equal("google", verified.Amr);
        Assert.Equal(Now.AddSeconds(30), verified.ExpiresAt);
    }

    [Fact]
    public void Other_audience_expired_unknown_issuer_and_oversized_fail()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ticket = ExternalSignInTicket.Sign(Claims(), key, Clock(Now));
        Assert.Null(ExternalSignInTicket.Verify(ticket, _ => (key, "https://os.intochat.io"), Clock(Now)));
        Assert.Null(ExternalSignInTicket.Verify(ticket, _ => (key, Audience), Clock(Now.AddSeconds(31))));
        Assert.Null(ExternalSignInTicket.Verify(ticket, _ => null, Clock(Now)));
        Assert.Null(ExternalSignInTicket.Verify(new string('a', ExternalSignInTicket.MaxTokenLength + 1), _ => (key, Audience), Clock(Now)));
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Null(ExternalSignInTicket.Verify(ticket, _ => (other, Audience), Clock(Now)));
    }

    [Fact]
    public void Lifetime_over_sixty_seconds_is_refused_at_signing()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal(60, ExternalSignInTicket.LifetimeSeconds);
        Assert.Equal(4096, ExternalSignInTicket.MaxTokenLength);
        Assert.Throws<ArgumentException>(() => ExternalSignInTicket.Sign(Claims(Now.AddSeconds(61)), key, Clock(Now)));
    }

    // Byte compatibility with the os issuer: the header and payload JSON are exactly the os bytes, base64url without padding,
    // and the signature is a 64-byte IEEE P1363 ES256 signature over "header.payload".
    [Fact]
    public void Wire_format_matches_the_os_bytes()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parts = ExternalSignInTicket.Sign(Claims(), key, Clock(Now)).Split('.');
        Assert.Equal(3, parts.Length);
        Assert.Equal("""{"alg":"ES256","typ":"JWT","kid":"cloud"}""", Encoding.UTF8.GetString(Decode(parts[0])));
        Assert.Equal(
            """{"iss":"cloud","aud":"https://test.intochat.io","exp":""" + Now.AddSeconds(30).ToUnixTimeSeconds()
            + ""","jti":"jti-1","email":"Ada@Example.com","name":"Ada","email_verified":true,"amr":"google"}""",
            Encoding.UTF8.GetString(Decode(parts[1])));
        var signature = Decode(parts[2]);
        Assert.Equal(64, signature.Length);
        Assert.True(key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.DoesNotContain(parts, part => part.Contains('=', StringComparison.Ordinal) || part.Contains('+', StringComparison.Ordinal) || part.Contains('/', StringComparison.Ordinal));
    }

    [Fact]
    public void Imported_spki_key_verifies_and_non_p256_is_refused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var imported = ExternalSignInTicket.ImportPublicKey(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        var ticket = ExternalSignInTicket.Sign(Claims(), key, Clock(Now));
        Assert.NotNull(ExternalSignInTicket.Verify(ticket, _ => (imported, Audience), Clock(Now)));
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.ThrowsAny<CryptographicException>(() => ExternalSignInTicket.ImportPublicKey(Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo())));
    }

    [Theory]
    [InlineData(" Ada@Example.COM ", "ada@example.com")]
    [InlineData("no-at", null)]
    [InlineData("a@b@c", null)]
    [InlineData("@b", null)]
    [InlineData("", null)]
    public void Normalize_email_trims_lowercases_and_refuses_malformed(string email, string? expected)
    {
        Assert.Equal(expected, ExternalSignInTicket.NormalizeEmail(email));
    }

    private static byte[] Decode(string value) =>
        Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - (value.Length % 4)) % 4));

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
