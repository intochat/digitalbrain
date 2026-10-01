using DigitalBrain.Microsoft.CSharp;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit;

public sealed class RunTokensFacts
{
    [Fact]
    public void ATokenNamesItsFileAndRunUntilItExpires()
    {
        var time = new MovableTime();
        var tokens = new RunTokens(Create(new CSharpDeploymentSettings()), time);

        var token = tokens.Issue("workspace/report", "run-1");

        Assert.Equal(("workspace/report", "run-1"), (tokens.Validate(token)!.File, tokens.Validate(token)!.Run));
        time.Now += RunTokens.Lifetime;
        Assert.Null(tokens.Validate(token));
    }

    [Fact]
    public void SilosSharingAKeyAcceptEachOthersTokensAndNoOtherKeyDoes()
    {
        var key = Convert.ToBase64String(new byte[32]);
        var issuer = new RunTokens(Create(new CSharpDeploymentSettings { RunTokenKey = key }), TimeProvider.System);
        var peer = new RunTokens(Create(new CSharpDeploymentSettings { RunTokenKey = key }), TimeProvider.System);
        var stranger = new RunTokens(Create(new CSharpDeploymentSettings()), TimeProvider.System);

        var token = issuer.Issue("f", "r");

        Assert.NotNull(peer.Validate(token));
        Assert.Null(stranger.Validate(token));
        Assert.Null(issuer.Validate("not.a-token"));
    }

    private sealed class MovableTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
