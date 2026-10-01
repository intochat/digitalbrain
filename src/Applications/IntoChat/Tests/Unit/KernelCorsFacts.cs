using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IntoChat.Tests.Unit;

public sealed class KernelCorsFacts
{
    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("https://example.test/path")]
    public void NoCredentialsPolicyIsCreatedWithoutAnExplicitOrigin(string origin)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration[KernelCors.AllowedOriginConfigurationKey] = origin;
        builder.AddKernelCors();
        using var services = builder.Services.BuildServiceProvider();
        Assert.Null(services.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy("shell"));
    }

    [Fact]
    public void ExplicitOriginsPermitSessionCookiesWithoutWildcardOrigins()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration[KernelCors.AllowedOriginConfigurationKey] = "http://localhost:27880";
        builder.AddKernelCors();
        using var services = builder.Services.BuildServiceProvider();
        var policy = services.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy("shell");
        Assert.NotNull(policy);
        Assert.True(policy.SupportsCredentials);
        Assert.False(policy.AllowAnyOrigin);
        Assert.Contains("http://127.0.0.1:27880", policy.Origins);
        Assert.DoesNotContain("https://foreign.example", policy.Origins);
    }
}
