using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Sdk.Integrations;
using DigitalBrain.Platform.Integrations;
using DigitalBrain.Sdk.Secrets;
using DigitalBrain.Platform.Secrets;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Microsoft.GitHub.Tests.Unit;

public sealed class AppAuthFacts
{
    [Fact]
    public async Task AppAuthSignsWithTheKeyReleasedFromTheRegistration()
    {
        var ct = TestContext.Current.CancellationToken;
        using var appKey = RSA.Create(2048);
        await using var brain = await Start(appKey.ExportPkcs8PrivateKeyPem(), "7", ct);
        var handler = new CapturingHandler();
        using var tokens = new GitHubInstallationTokens(brain.SiloServices.GetRequiredService<GitHubAppRegistration>(), handler, null);

        await Assert.ThrowsAsync<GitHubAccessDeniedException>(() => tokens.GetTokenAsync(Binding(appId: 7), refresh: true, ct));

        var jwt = Assert.IsType<string>(handler.Bearer).Split('.');
        Assert.True(appKey.VerifyData(Encoding.ASCII.GetBytes($"{jwt[0]}.{jwt[1]}"), Base64UrlDecode(jwt[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.Equal("7", JsonDocument.Parse(Base64UrlDecode(jwt[1])).RootElement.GetProperty("iss").GetString());
        Assert.Equal("/app/installations/9/access_tokens", handler.Path);
    }

    [Fact]
    public async Task AnUnregisteredAppReportsUnavailableAndSendsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(null, null, ct);
        var handler = new CapturingHandler();
        using var tokens = new GitHubInstallationTokens(brain.SiloServices.GetRequiredService<GitHubAppRegistration>(), handler, null);

        await Assert.ThrowsAsync<GitHubUnavailableException>(() => tokens.GetTokenAsync(Binding(appId: 7), refresh: true, ct));

        Assert.Null(handler.Path);
    }

    [Fact]
    public async Task ABindingOfAnotherAppIsNeverSignedForByTheRegisteredKey()
    {
        var ct = TestContext.Current.CancellationToken;
        using var appKey = RSA.Create(2048);
        await using var brain = await Start(appKey.ExportPkcs8PrivateKeyPem(), "7", ct);
        var handler = new CapturingHandler();
        using var tokens = new GitHubInstallationTokens(brain.SiloServices.GetRequiredService<GitHubAppRegistration>(), handler, null);

        await Assert.ThrowsAsync<GitHubUnavailableException>(() => tokens.GetTokenAsync(Binding(appId: 8), refresh: true, ct));

        Assert.Null(handler.Path);
    }

    private static Task<UnitBrain> Start(string? privateKeyPem, string? appId, CancellationToken cancellationToken)
    {
        var seeds = new Dictionary<string, string?>();
        if (privateKeyPem is not null)
        {
            seeds["DigitalBrain:Integrations:github:PrivateKeyPem"] = privateKeyPem;
            seeds["DigitalBrain:Integrations:github:AppId"] = appId;
        }

        return UnitTest.Create()
            .WithExecution(new TestExecutionOptions { PrivateConfiguration = seeds })
            .WithModule<SecretsModule>()
            .WithModule<IntegrationsModule>()
            .WithModule<GitHubModule>()
            .StartAsync(cancellationToken);
    }

    private static GitHubRepositoryBinding Binding(long appId)
        => new("repo", 11, 9, appId, "intochat", "digitalbrain", "0123456789abcdef");

    private static byte[] Base64UrlDecode(string text)
        => Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/').PadRight((text.Length + 3) / 4 * 4, '='));

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Bearer { get; private set; }

        public string? Path { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bearer = request.Headers.Authorization?.Parameter;
            Path = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
    }
}
