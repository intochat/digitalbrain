using DigitalBrain.Core;
using IntoChat.Activity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntoChat.Tests;

public sealed class ActivityEndpointFacts
{
    [Fact]
    public void SnapshotIsScopedByOwnerAndWorkspace()
    {
        var feed = new ActivityFeed();
        var one = ActivityEndpoints.ScopeId("owner", "one");
        var two = ActivityEndpoints.ScopeId("owner", "two");
        feed.Append(new ActivityEvent(one, 0, Guid.NewGuid(), Guid.NewGuid(), null,
            DateTimeOffset.UtcNow, NeuronActivityKind.SignalPublished, "source", null, "Changed", "published", null, null));

        Assert.Single(ActivityEndpoints.ReadSnapshot(feed, "owner", "one").Events);
        Assert.Empty(ActivityEndpoints.ReadSnapshot(feed, "owner", "two").Events);
        Assert.NotEqual(one, two);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../secret")]
    public void InvalidWorkspaceIsRejected(string workspace)
    {
        Assert.Throws<ArgumentException>(() => ActivityEndpoints.ScopeId("owner", workspace));
    }

    [Fact]
    public async Task HttpRouteRequiresCredentialsAndStreamsActivity()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.Configure<BasicAuthOptions>(options =>
        {
            options.Username = "owner";
            options.Password = "secret";
        });
        var feed = new ActivityFeed();
        builder.Services.AddSingleton(feed);
        await using var app = builder.Build();
        app.UseBasicAuthGate();
        app.MapNeuronActivity();
        await app.StartAsync(TestContext.Current.CancellationToken);
        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient { BaseAddress = new Uri(address) };

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
            (await http.GetAsync("/workspaces/one/activity", TestContext.Current.CancellationToken)).StatusCode);
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("owner:secret")));
        feed.Append(new ActivityEvent(ActivityEndpoints.ScopeId("owner", "one"), 0, Guid.NewGuid(), Guid.NewGuid(),
            null, DateTimeOffset.UtcNow, NeuronActivityKind.SignalPublished, "source", null, "Changed", "published", null, null));
        var response = await http.GetStringAsync("/workspaces/one/activity", TestContext.Current.CancellationToken);
        Assert.Contains("Changed", response);
        Assert.DoesNotContain("source", await http.GetStringAsync("/workspaces/two/activity", TestContext.Current.CancellationToken));

        for (var i = 0; i < 2000; i++)
        {
            feed.Append(new ActivityEvent(ActivityEndpoints.ScopeId("owner", "one"), 0, Guid.NewGuid(), Guid.NewGuid(),
                null, DateTimeOffset.UtcNow, NeuronActivityKind.SignalPublished, "source", null, "Changed", "published", null, null));
        }
        using var streamResponse = await http.GetAsync("/workspaces/one/activity/events?after=0",
            HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        using var reader = new StreamReader(await streamResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
        Assert.Equal("event: gap", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }
}
