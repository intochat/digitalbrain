using System.Text;
using System.Text.Json;
using TripRadar.Bot.Auth;

namespace TripRadar.Bot.Tests.Auth;

public class TokenClaimsReaderTests
{
    // Build a minimal unsigned JWT with the given payload fields.
    // Format: base64url(header).base64url(payload).fakesig
    // JwtSecurityTokenHandler.CanReadToken only requires valid base64url header+payload.
    private static string BuildToken(object payload)
    {
        var header = Base64UrlEncode("""{"alg":"none","typ":"JWT"}""");
        var body = Base64UrlEncode(JsonSerializer.Serialize(payload));
        return $"{header}.{body}.";
    }

    private static string Base64UrlEncode(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    [Fact]
    public void ReadUsernameAndExpiry_ValidTokenWithNameClaim_ExtractsUsername()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);
        var token = BuildToken(new
        {
            unique_name = "alice",
            exp = expiry.ToUnixTimeSeconds()
        });

        var result = TokenClaimsReader.ReadUsernameAndExpiry(token);

        result.Success.Should().BeTrue();
        result.Value.Username.Should().Be("alice");
    }

    [Fact]
    public void ReadUsernameAndExpiry_ValidTokenWithExpClaim_ExtractsExpiry()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(2);
        var token = BuildToken(new
        {
            unique_name = "bob",
            exp = expiry.ToUnixTimeSeconds()
        });

        var result = TokenClaimsReader.ReadUsernameAndExpiry(token);

        result.Success.Should().BeTrue();
        result.Value.ExpiresAtUtc.ToUnixTimeSeconds().Should().Be(expiry.ToUnixTimeSeconds());
    }

    [Fact]
    public void ReadUsernameAndExpiry_MalformedToken_ReturnsFailure()
    {
        var result = TokenClaimsReader.ReadUsernameAndExpiry("not.a.valid.jwt.at.all.x.y.z");

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ReadUsernameAndExpiry_EmptyToken_ReturnsFailure()
    {
        var result = TokenClaimsReader.ReadUsernameAndExpiry("");

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ReadUsernameAndExpiry_TokenWithoutUsernameClaim_ReturnsFailure()
    {
        var token = BuildToken(new { sub = "12345", exp = 9999999999L });

        var result = TokenClaimsReader.ReadUsernameAndExpiry(token);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("username");
    }
}
