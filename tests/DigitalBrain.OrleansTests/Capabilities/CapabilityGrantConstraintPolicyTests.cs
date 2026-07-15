using System.Text.Json;
using DigitalBrain.Kernel.Capabilities;

namespace DigitalBrain.OrleansTests.Capabilities;

public sealed class CapabilityGrantConstraintPolicyTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("ACCESS_TOKEN")]
    [InlineData("refresh-token")]
    [InlineData("Authorization")]
    [InlineData("api.key")]
    [InlineData("private key")]
    [InlineData("credential")]
    [InlineData("Credentials")]
    [InlineData("token")]
    [InlineData("Client-Secret")]
    [InlineData("secret")]
    [InlineData("secret_value")]
    [InlineData("action-token")]
    [InlineData("auth token")]
    [InlineData("bearer.token")]
    [InlineData("id-token")]
    [InlineData("session_token")]
    [InlineData("secret-key")]
    [InlineData("connection string")]
    [InlineData("passphrase")]
    [InlineData("authorization code")]
    [InlineData("code-verifier")]
    [InlineData("secret access key")]
    [InlineData("private_key_pem")]
    [InlineData("sas-token")]
    [InlineData("session id")]
    public void Credential_like_property_names_are_rejected_recursively_after_normalization(string propertyName)
    {
        using var document = JsonDocument.Parse($$"""
            {
              "allowedToolIds": ["feature.tool"],
              "payload": {
                "envelope": {
                  "{{propertyName}}": ["credential-canary-value"]
                }
              }
            }
            """);

        Assert.Throws<ArgumentException>(() =>
            CapabilityGrantConstraintPolicy.CopyValidated(document.RootElement));
    }

    [Fact]
    public void Noncredential_property_names_and_arbitrary_allowlist_values_remain_valid()
    {
        using var document = JsonDocument.Parse("""
            {
              "allowedToolIds": ["feature.tool"],
              "payload": {
                "tokenBucketId": ["credential-canary-value"],
                "passwordPolicyId": ["standard"],
                "secretRotationId": ["credential-canary-value"],
                "actionTokenPolicyId": ["standard"],
                "sessionTokenBucketId": ["credential-canary-value"],
                "connectionStringPolicyId": ["standard"]
              }
            }
            """);

        var validated = CapabilityGrantConstraintPolicy.CopyValidated(document.RootElement);

        Assert.Equal(JsonValueKind.Object, validated.ValueKind);
    }
}
