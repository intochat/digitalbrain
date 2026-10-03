using System.Net;
using System.Text.Json;
using DigitalBrain.Google.Gmail;
using DigitalBrain.Testing.Module;
using Xunit;

namespace DigitalBrain.Modules.Google.Gmail.Tests;

// The configured-registration fact lives in GmailConfiguredRegistrationFacts on the shared host.
public sealed class GmailOAuthCallbackFacts
{
    [Fact]
    public async Task ConnectingGmailAgainstAnUnconfiguredRegistrationExplainsInsteadOfCrashing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var stub = await TokenEndpointStub.StartAsync(ct);
        await using var brain = await ModuleTest.Create()
            .WithModule<GmailModule, GmailModuleOptions>(options => options.TokenEndpoint = stub.TokenEndpoint)
            .WithHttpEdge()
            .StartAsync(ct);

        foreach (var path in new[] { "google/gmail/oauth/callback?code=fake-code", "integrations/gmail/login?request=x" })
        {
            using var response = await brain.HttpClient.GetAsync(path, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var json = JsonDocument.Parse(body);
            Assert.Equal("gmail", json.RootElement.GetProperty("integration").GetString());
            Assert.Equal("Unconfigured", json.RootElement.GetProperty("status").GetString());
            Assert.Equal(["ClientId", "ClientSecret", "PublicOrigin"],
                json.RootElement.GetProperty("missing").EnumerateArray().Select(field => field.GetString()).ToArray());
            Assert.DoesNotContain("Aspire", body, StringComparison.Ordinal);
        }
    }
}
