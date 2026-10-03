using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Azure.Core;
using DigitalBrain.Microsoft.CSharp;
using Xunit;
using static Microsoft.Extensions.Options.Options;

namespace DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit;

public sealed class SessionPoolRunnerFacts
{
    private const string Pool = "https://pool.env.westeurope.azurecontainerapps.io";

    [Fact]
    public async Task EachOwnerGetsItsOwnSessionAndEveryCallCarriesThePoolToken()
    {
        var ct = TestContext.Current.CancellationToken;
        var pool = new FakePool();
        var runner = Runner(pool);

        await runner.StartAsync(new CSharpRun("alice", "run-a", "Console.WriteLine(1);", new Dictionary<string, string>()), ct);
        await runner.StartAsync(new CSharpRun("bob", "run-b", "Console.WriteLine(2);", new Dictionary<string, string>()), ct);

        var alice = SessionPoolRunner.Session("alice");
        Assert.Contains(pool.Requests, request => request.Url == $"{Pool}/runs/run-a?{alice}");
        Assert.Contains(pool.Requests, request => request.Url == $"{Pool}/runs/run-b?{SessionPoolRunner.Session("bob")}");
        Assert.All(pool.Requests, request => Assert.Equal("Bearer pool-token", request.Authorization));
    }

    [Fact]
    public async Task ReadingAnOwnerWithoutASessionNeverAllocatesOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var pool = new FakePool();
        var runner = Runner(pool);

        Assert.Equal(CSharpFileStatus.Stopped, (await runner.InspectAsync("carol", "run-c", ct)).Status);
        Assert.Equal("", await runner.LogsAsync("carol", "run-c", 10, ct));
        await runner.StopAsync("carol", "run-c", ct);

        Assert.All(pool.Requests, request => Assert.Contains("/.management/getSession?", request.Url, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnExistingSessionIsInspectedThroughThePool()
    {
        var ct = TestContext.Current.CancellationToken;
        var pool = new FakePool();
        var runner = Runner(pool);
        await runner.StartAsync(new CSharpRun("alice", "run-a", "x", new Dictionary<string, string>()), ct);

        Assert.Equal(CSharpFileStatus.Running, (await runner.InspectAsync("alice", "run-a", ct)).Status);
    }

    [Fact]
    public void SessionIdentifiersHideThePrincipalAndFitThePoolRules()
    {
        var session = SessionPoolRunner.Session("alice@contoso.com");

        Assert.Matches("^identifier=u-[0-9a-f]{32}$", session);
        Assert.DoesNotContain("alice", session, StringComparison.Ordinal);
    }

    private static SessionPoolRunner Runner(FakePool pool)
        => new(new HttpClient(pool), new FixedCredential(), Create(new CSharpDeploymentSettings { SessionPoolEndpoint = Pool }));

    private sealed class FixedCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("pool-token", DateTimeOffset.MaxValue);

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    // A session exists once a request other than a management read has reached its identifier.
    private sealed class FakePool : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, bool> _sessions = new(StringComparer.Ordinal);

        public ConcurrentQueue<(string Url, string? Authorization)> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Enqueue((url, request.Headers.Authorization?.ToString()));
            var session = request.RequestUri.Query.Split('&').First(part => part.TrimStart('?').StartsWith("identifier=", StringComparison.Ordinal)).TrimStart('?');
            if (request.RequestUri.AbsolutePath == "/.management/getSession")
            {
                return Task.FromResult(new HttpResponseMessage(_sessions.ContainsKey(session) ? HttpStatusCode.OK : HttpStatusCode.BadRequest));
            }
            _sessions[session] = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { identifier = "run", status = "Running", exitCode = (int?)null, startedAt = DateTimeOffset.UnixEpoch }),
            });
        }
    }
}
