using System.Security.Claims;
using Brain.Contracts;
using Brain.Modules.Flutter;
using Xunit;

namespace DigitalBrain.Tests.Flutter;

public sealed class FlutterGatewayTests
{
    private const string Owner = "owner-a";
    private const string Space = "actor/flutter";
    private const string Target = "owner-a|actor/flutter|chat/main";
    private const string Contract = "chat.post.v1";

    [Fact]
    public void Missing_authentication_is_rejected()
    {
        var exception = Assert.Throws<BrainException>(() =>
            FlutterGatewaySession.FromPrincipal(new ClaimsPrincipal()));

        Assert.Equal("auth.required", exception.Code);
    }

    [Fact]
    public void Owner_and_space_are_derived_from_the_authenticated_session()
    {
        var session = FlutterGatewaySession.FromPrincipal(Principal(Contract));

        Assert.Equal(Owner, session.OwnerId);
        Assert.Equal(Space, session.SpaceId);
        Assert.Equal("owner-a|actor/flutter|session/flutter", session.CallerKey);
    }

    [Fact]
    public void Cross_owner_target_is_rejected()
    {
        var policy = new FlutterGatewayPolicy();

        var exception = Assert.Throws<BrainException>(() => policy.AuthorizeMutation(
            FlutterGatewaySession.FromPrincipal(Principal(Contract)),
            "owner-b|actor/flutter|chat/main",
            Contract,
            "{}",
            "command-cross-owner"));

        Assert.Equal(BrainErrors.GrantDenied, exception.Code);
    }

    [Fact]
    public void Ungranted_contract_is_rejected()
    {
        var policy = new FlutterGatewayPolicy();

        var exception = Assert.Throws<BrainException>(() => policy.AuthorizeMutation(
            FlutterGatewaySession.FromPrincipal(Principal()),
            Target,
            Contract,
            "{}",
            "command-ungranted"));

        Assert.Equal(BrainErrors.GrantMissing, exception.Code);
    }

    [Fact]
    public void Oversized_input_is_rejected()
    {
        var policy = new FlutterGatewayPolicy();

        var exception = Assert.Throws<BrainException>(() => policy.AuthorizeMutation(
            FlutterGatewaySession.FromPrincipal(Principal(Contract)),
            Target,
            Contract,
            JsonWithText(32_769),
            "command-oversized"));

        Assert.Equal("input.invalid", exception.Code);
    }

    [Fact]
    public void Replayed_mutation_command_id_is_rejected()
    {
        var policy = new FlutterGatewayPolicy();
        var session = FlutterGatewaySession.FromPrincipal(Principal(Contract));

        policy.AuthorizeMutation(session, Target, Contract, "{}", "command-replay");
        var exception = Assert.Throws<BrainException>(() =>
            policy.AuthorizeMutation(session, Target, Contract, "{}", "command-replay"));

        Assert.Equal("command.replayed", exception.Code);
    }

    [Fact]
    public void App_host_contains_no_flutter_process_port_or_gateway_details()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "hosts",
            "DigitalBrain.AppHost",
            "AppHost.cs"));

        Assert.Contains("WithDigitalBrainFlutter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("flutter run", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("5320", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Brain.UiGateway", source, StringComparison.Ordinal);
    }

    private static ClaimsPrincipal Principal(params string[] grants)
    {
        var claims = new List<Claim>
        {
            new("digitalbrain:owner", Owner),
            new("digitalbrain:space", Space)
        };
        claims.AddRange(grants.Select(grant => new Claim("digitalbrain:grant", grant)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static string JsonWithText(int length) =>
        System.Text.Json.JsonSerializer.Serialize(new { text = new string('x', length) });

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
