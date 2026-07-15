extern alias McpProject;

using System.Security.Claims;
using DigitalBrain.Kernel.Contracts.Runtime;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using UiDevelopmentLoginAuthenticator = McpProject::DigitalBrain.Mcp.UiDevelopmentLoginAuthenticator;
using UiDevelopmentLoginOptions = McpProject::DigitalBrain.Mcp.UiDevelopmentLoginOptions;
using UiExternalIdentityOptions = McpProject::DigitalBrain.Mcp.UiExternalIdentityOptions;

namespace DigitalBrain.Tests.Runtime;

public sealed class UiExternalIdentityTests
{
    [Fact]
    public void Production_forbids_development_login_configuration_and_requires_complete_external_oidc_configuration()
    {
        var usernameConfiguration = Configuration(new Dictionary<string, string?>
        {
            ["DigitalBrain:Runtime:Ui:DevelopmentUsername"] = "developer"
        });
        Assert.Throws<InvalidOperationException>(() =>
            UiDevelopmentLoginOptions.FromConfiguration(usernameConfiguration, RuntimeProfile.Production));

        var passwordConfiguration = Configuration(new Dictionary<string, string?>
        {
            ["DigitalBrain:Runtime:Ui:DevelopmentPassword"] = string.Empty
        });
        Assert.Throws<InvalidOperationException>(() =>
            UiDevelopmentLoginOptions.FromConfiguration(passwordConfiguration, RuntimeProfile.Production));

        var missingOidc = Configuration(new Dictionary<string, string?>());
        Assert.Throws<InvalidOperationException>(() =>
            UiExternalIdentityOptions.FromConfiguration(missingOidc, RuntimeProfile.Production));

        var partialOidc = Configuration(new Dictionary<string, string?>
        {
            ["DigitalBrain:Runtime:Ui:Oidc:Issuer"] = "https://issuer.example/tenant",
            ["DigitalBrain:Runtime:Ui:Oidc:Audience"] = "digitalbrain-ui"
        });
        Assert.Throws<InvalidOperationException>(() =>
            UiExternalIdentityOptions.FromConfiguration(partialOidc, RuntimeProfile.Production));

        var claimOnly = Configuration(new Dictionary<string, string?>
        {
            ["DigitalBrain:Runtime:Ui:Oidc:SubjectClaim"] = "custom_subject"
        });
        Assert.Throws<InvalidOperationException>(() =>
            UiExternalIdentityOptions.FromConfiguration(claimOnly, RuntimeProfile.Development));
    }

    [Fact]
    public void Development_login_defaults_are_bounded_exact_and_production_authenticator_is_disabled()
    {
        var defaults = UiDevelopmentLoginOptions.FromConfiguration(
            Configuration(new Dictionary<string, string?>()),
            RuntimeProfile.Development);
        var authenticator = new UiDevelopmentLoginAuthenticator(defaults);

        Assert.True(authenticator.TryAuthenticate("admin", "admin", out var defaultContext));
        Assert.Equal("local-owner", defaultContext.OwnerId.Value);
        Assert.Equal("flutter-ui", defaultContext.ActorId.Value);
        Assert.False(authenticator.TryAuthenticate("wrong", "admin", out _));
        Assert.False(authenticator.TryAuthenticate("admin", "wrong", out _));
        Assert.False(authenticator.TryAuthenticate(string.Empty, "admin", out _));
        Assert.False(authenticator.TryAuthenticate("admin", string.Empty, out _));
        Assert.False(authenticator.TryAuthenticate(new string('a', 257), "admin", out _));
        Assert.False(authenticator.TryAuthenticate("admin", new string('a', 257), out _));

        var configured = Configuration(new Dictionary<string, string?>
        {
            ["DigitalBrain:Runtime:Ui:DevelopmentUsername"] = "developer",
            ["DigitalBrain:Runtime:Ui:DevelopmentPassword"] = "password",
            ["DigitalBrain:Runtime:Ui:OwnerId"] = "owner",
            ["DigitalBrain:Runtime:Ui:ActorId"] = "developer"
        });
        var configuredAuthenticator = new UiDevelopmentLoginAuthenticator(
            UiDevelopmentLoginOptions.FromConfiguration(configured, RuntimeProfile.Test));
        Assert.True(configuredAuthenticator.TryAuthenticate("developer", "password", out var configuredContext));
        Assert.Equal("owner", configuredContext.OwnerId.Value);
        Assert.Equal("developer", configuredContext.ActorId.Value);

        var productionOptions = UiDevelopmentLoginOptions.FromConfiguration(
            Configuration(new Dictionary<string, string?>()),
            RuntimeProfile.Production);
        Assert.False(productionOptions.Enabled);
        Assert.False(new UiDevelopmentLoginAuthenticator(productionOptions)
            .TryAuthenticate("admin", "admin", out _));
    }

    [Theory]
    [InlineData("", "admin")]
    [InlineData("admin", "")]
    public void Development_login_configuration_rejects_empty_credentials(string username, string password)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["DigitalBrain:Runtime:Ui:DevelopmentUsername"] = username,
            ["DigitalBrain:Runtime:Ui:DevelopmentPassword"] = password
        });

        Assert.Throws<InvalidOperationException>(() =>
            UiDevelopmentLoginOptions.FromConfiguration(configuration, RuntimeProfile.Development));
    }

    [Fact]
    public void Development_login_configuration_rejects_oversized_credentials()
    {
        var oversizedUsername = Configuration(new Dictionary<string, string?>
        {
            ["DigitalBrain:Runtime:Ui:DevelopmentUsername"] = new string('a', 257)
        });
        var oversizedPassword = Configuration(new Dictionary<string, string?>
        {
            ["DigitalBrain:Runtime:Ui:DevelopmentPassword"] = new string('a', 257)
        });

        Assert.Throws<InvalidOperationException>(() =>
            UiDevelopmentLoginOptions.FromConfiguration(oversizedUsername, RuntimeProfile.Development));
        Assert.Throws<InvalidOperationException>(() =>
            UiDevelopmentLoginOptions.FromConfiguration(oversizedPassword, RuntimeProfile.Development));
    }

    [Fact]
    public void Oidc_configuration_enforces_framework_signature_issuer_audience_and_lifetime_validation()
    {
        var options = ExternalOptions();
        var jwt = new JwtBearerOptions();

        options.Configure(jwt);

        Assert.Equal("https://issuer.example/tenant", jwt.Authority);
        Assert.Equal("digitalbrain-ui", jwt.Audience);
        Assert.True(jwt.RequireHttpsMetadata);
        Assert.False(jwt.MapInboundClaims);
        Assert.False(jwt.IncludeErrorDetails);
        Assert.False(jwt.SaveToken);
        Assert.True(jwt.TokenValidationParameters.ValidateIssuer);
        Assert.Equal("https://issuer.example/tenant", jwt.TokenValidationParameters.ValidIssuer);
        Assert.True(jwt.TokenValidationParameters.ValidateAudience);
        Assert.True(jwt.TokenValidationParameters.ValidateIssuerSigningKey);
        Assert.True(jwt.TokenValidationParameters.ValidateLifetime);
        Assert.True(jwt.TokenValidationParameters.RequireSignedTokens);
        Assert.True(jwt.TokenValidationParameters.RequireExpirationTime);
        Assert.Equal("digitalbrain-ui", jwt.TokenValidationParameters.ValidAudience);
    }

    [Fact]
    public void Validated_external_claims_map_to_exact_runtime_scope_and_allowlisted_grants()
    {
        var options = ExternalOptions();
        var principal = Principal(
            new("sub", "subject"),
            new("digitalbrain_grants", "brain.read ui.action"));

        Assert.True(options.TryMapPrincipal(principal, out var context));
        Assert.Equal(BrainOwnerId.FromExternalIdentity(options.Issuer, "subject"), context.OwnerId);
        Assert.Equal(ActorId.FromExternalIdentity(options.Issuer, "subject"), context.ActorId);
        Assert.Equal(AuthAssurance.Oidc, context.Assurance);
        Assert.Equal(["brain.read", "ui.action"], context.Grants.Order(StringComparer.Ordinal).ToArray());

        var unknownGrant = Principal(
            new("sub", "subject"),
            new("digitalbrain_grants", "brain.admin"));
        Assert.False(options.TryMapPrincipal(unknownGrant, out _));

        var ambiguousSubject = Principal(
            new("sub", "subject-a"),
            new("sub", "subject-b"),
            new("digitalbrain_grants", "brain.read"));
        Assert.False(options.TryMapPrincipal(ambiguousSubject, out _));

        var repeatedSubject = Principal(
            new("sub", "subject"),
            new("sub", "subject"),
            new("digitalbrain_grants", "brain.read"));
        Assert.False(options.TryMapPrincipal(repeatedSubject, out _));

        var multipleAuthenticatedIdentities = new ClaimsPrincipal([
            (ClaimsIdentity)principal.Identity!,
            new ClaimsIdentity([new Claim("sub", "other")], "other-oidc")
        ]);
        Assert.False(options.TryMapPrincipal(multipleAuthenticatedIdentities, out _));

        var normalizedIdentity = Principal(
            new("sub", "subject "),
            new("digitalbrain_grants", "brain.read"));
        Assert.False(options.TryMapPrincipal(normalizedIdentity, out _));
    }

    [Fact]
    public void Production_rejects_non_https_issuer_and_overlapping_claim_mappings()
    {
        var insecure = ExternalConfiguration();
        insecure["DigitalBrain:Runtime:Ui:Oidc:Issuer"] = "http://issuer.example/tenant";
        Assert.Throws<InvalidOperationException>(() =>
            UiExternalIdentityOptions.FromConfiguration(insecure, RuntimeProfile.Production));

        var overlapping = ExternalConfiguration();
        overlapping["DigitalBrain:Runtime:Ui:Oidc:SubjectClaim"] = "digitalbrain_grants";
        Assert.Throws<InvalidOperationException>(() =>
            UiExternalIdentityOptions.FromConfiguration(overlapping, RuntimeProfile.Production));
    }

    private static UiExternalIdentityOptions ExternalOptions() =>
        UiExternalIdentityOptions.FromConfiguration(ExternalConfiguration(), RuntimeProfile.Production);

    private static IConfigurationRoot ExternalConfiguration() => Configuration(new Dictionary<string, string?>
    {
        ["DigitalBrain:Runtime:Ui:Oidc:Issuer"] = "https://issuer.example/tenant",
        ["DigitalBrain:Runtime:Ui:Oidc:Audience"] = "digitalbrain-ui",
        ["DigitalBrain:Runtime:Ui:Oidc:AllowedGrants"] = "brain.read,ui.action"
    });

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "oidc"));

    private static IConfigurationRoot Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
