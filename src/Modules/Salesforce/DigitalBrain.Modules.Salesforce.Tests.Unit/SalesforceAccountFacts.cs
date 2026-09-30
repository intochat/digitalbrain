using DigitalBrain.Core.Enforcement;
using DigitalBrain.Salesforce;
using DigitalBrain.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Salesforce.Tests.Unit;

public sealed class SalesforceAccountFacts
{
    [Fact]
    public void SalesforceServesOnlyItsServiceRoutesUnderTheAccountsRoute()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(typeof(IGrainFactory), _ => null!);
        IEndpointRouteBuilder app = builder.Build();
        new SalesforceModule().Configure(app);

        var routes = app.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => $"{string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods)} {endpoint.RoutePattern.RawText}").Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                "GET /brains/{brainId}/integrations/accounts/services",
                "POST /brains/{brainId}/integrations/accounts/services/{provider}/start",
            ],
            routes);
    }

    [Fact]
    public void AuthorizationStartRequiresConfiguredProviderAndMintsDistinctCapabilities()
    {
        var unavailable = new Logins(null);
        Assert.False(unavailable.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => unavailable.Require());
        var logins = new Logins(new Uri("https://example.test"));
        var first = logins.Require();
        Assert.True(logins.IsConfigured);
        Assert.Equal("/integrations/test/login", first.AbsolutePath);
        Assert.StartsWith("?request=", first.Query, StringComparison.Ordinal);
        Assert.NotEqual(first, logins.Require());
    }

    [Fact]
    public void RegistryScopeSeparatesAccountsAndBrains()
    {
        Assert.NotEqual(BrainScope.Create("alice", "one").Id, BrainScope.Create("bob", "one").Id);
        Assert.NotEqual(BrainScope.Create("alice", "one").Id, BrainScope.Create("alice", "two").Id);
        var scope = new BrowserLoginWorkspace(BrainScope.Create("alice", "one").Id);
        Assert.Equal(scope, BrowserLoginWorkspace.FromScope(scope.ToScope()));
        Assert.Null(BrowserLoginWorkspace.FromScope("compose"));
    }

    private sealed class Logins(Uri? origin) : BrowserLogins(new("test", "Test", "test", "/integrations/test/login", "/integrations/test/callback", "Test"))
    {
        protected override Uri? PublicOrigin => origin;
    }
}
