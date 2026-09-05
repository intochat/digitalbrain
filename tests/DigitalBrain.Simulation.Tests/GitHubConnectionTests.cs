using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;
using DigitalBrain.Simulation.Tests.Sdk;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DigitalBrain.Simulation.Tests;

public sealed class GitHubConnectionTests
{
    private static readonly ActorContext Actor = new(PrincipalId.New(), "github-owner");
    private static readonly OwnerId Owner = new("github-tests");
    private static readonly string Head = new('a', 40);
    private static readonly string Base = new('b', 40);
    private static readonly string Merge = new('c', 40);
    private static readonly string Key = RSA.Create(2048).ExportRSAPrivateKeyPem();

    [Fact]
    public void Binding_has_stable_numeric_identity_and_no_secret_record_representation()
    {
        var binding = Binding();
        var registry = new GitHubRepositoryBindings([binding]);
        var id = new NeuronId("repository", Owner, binding.InstanceName);
        Assert.EndsWith("github-source-42", id.Name, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, binding.ToString()!, StringComparison.Ordinal);
        using var actor = VerifiedActor.Enter(Actor);
        Assert.Same(binding, registry.GetFor(id));
        Assert.Equal(binding.Revision, Binding().Revision);
        Assert.NotEqual(binding.Revision, Binding(privateKey: RSA.Create(2048).ExportRSAPrivateKeyPem()).Revision);
        Assert.Throws<McpOperationException>(() => registry.GetFor(new NeuronId("repository", Owner, PrincipalPartition.InstanceName(PrincipalId.New(), binding.LocalName))));
        binding.Revoke();
        Assert.Throws<McpOperationException>(() => registry.GetFor(id));
    }

    [Fact]
    public void App_jwt_uses_rs256_with_clock_skew_and_bounded_lifetime()
    {
        var now = DateTimeOffset.UtcNow;
        var jwt = GitHubInstallationTokens.CreateAppJwt(Binding(), now).Split('.');
        using var payload = JsonDocument.Parse(Decode(jwt[1]));
        Assert.Equal("7", payload.RootElement.GetProperty("iss").GetString());
        Assert.Equal(now.AddSeconds(-60).ToUnixTimeSeconds(), payload.RootElement.GetProperty("iat").GetInt64());
        Assert.Equal(now.AddMinutes(9).ToUnixTimeSeconds(), payload.RootElement.GetProperty("exp").GetInt64());
        using var rsa = RSA.Create(); rsa.ImportFromPem(Key);
        Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes($"{jwt[0]}.{jwt[1]}"), Decode(jwt[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public async Task Delegation_metadata_is_visible_only_to_bound_principal_and_preserves_generic_schema()
    {
        var binding = Binding();
        var inner = new AgentDelegation<IRepository>("ask_repository", "Private repository acme/brain", binding.LocalName, Owner);
        var source = new GitHubRepositoryDelegation(binding, inner);
        var requests = new CapturingRequests();
        using var actor = VerifiedActor.Enter(Actor);
        using var context = new AgentToolContext(new NeuronId("assistant", Owner, PrincipalPartition.InstanceName(Actor.PrincipalId, "assistant")), Actor.PrincipalId, requests);
        var tool = Assert.Single((await source.GetToolsAsync(context, TestContext.Current.CancellationToken)).OfType<AIFunction>());
        var original = Assert.Single((await inner.GetToolsAsync(context, TestContext.Current.CancellationToken)).OfType<AIFunction>());
        Assert.True(JsonElement.DeepEquals(original.JsonSchema, tool.JsonSchema));
        _ = await tool.InvokeAsync(new() { ["request"] = "Review PR 12" }, TestContext.Current.CancellationToken);
        Assert.Equal(binding.InstanceName, requests.Instance);
        Assert.Equal("Review PR 12", requests.Request?.Text);
        using (var otherActor = VerifiedActor.Enter(new ActorContext(PrincipalId.New(), "same-owner-different-principal")))
        using (var other = new AgentToolContext(new NeuronId("assistant", Owner, "other"), VerifiedActor.Current!.PrincipalId, requests))
        {
            Assert.Empty(await source.GetToolsAsync(other, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<McpOperationException>(() => tool.InvokeAsync(new() { ["request"] = "Wrong actor" }, TestContext.Current.CancellationToken).AsTask());
        }
        binding.BeginRecovery();
        Assert.Empty(await source.GetToolsAsync(context, TestContext.Current.CancellationToken));
        binding.CompleteRecovery();
        binding.Revoke();
        Assert.Empty(await source.GetToolsAsync(context, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<McpOperationException>(() => tool.InvokeAsync(new() { ["request"] = "After revocation" }, TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(1, requests.Calls);
    }

    [Fact]
    public async Task Installation_tokens_are_read_only_repository_scoped_and_refreshed_only_on_demand()
    {
        using var fixture = new Fixture();
        using var actor = VerifiedActor.Enter(Actor);
        Assert.Equal("installation-token", await fixture.Tokens.GetTokenAsync(fixture.Binding, false, TestContext.Current.CancellationToken));
        _ = await fixture.Tokens.GetTokenAsync(fixture.Binding, false, TestContext.Current.CancellationToken);
        Assert.Equal(1, fixture.TokenCalls);
        _ = await fixture.Tokens.GetTokenAsync(fixture.Binding, true, TestContext.Current.CancellationToken);
        Assert.Equal(2, fixture.TokenCalls);
        Assert.Equal(42, fixture.TokenRequest!.Value.GetProperty("repository_ids")[0].GetInt64());
        Assert.All(fixture.TokenRequest.Value.GetProperty("permissions").EnumerateObject(), permission => Assert.Equal("read", permission.Value.GetString()));
        using var other = VerifiedActor.Enter(new ActorContext(PrincipalId.New(), "other"));
        await Assert.ThrowsAsync<McpOperationException>(() => fixture.Tokens.GetTokenAsync(fixture.Binding, false, TestContext.Current.CancellationToken));
        Assert.Equal(2, fixture.TokenCalls);
    }

    [Fact]
    public async Task Token_response_with_write_scope_is_rejected_and_auth_post_is_not_retried()
    {
        using var fixture = new Fixture { TokenWritePermission = true };
        using var actor = VerifiedActor.Enter(Actor);
        await Assert.ThrowsAsync<McpOperationException>(() => fixture.Tokens.GetTokenAsync(fixture.Binding, false, TestContext.Current.CancellationToken));
        Assert.Equal(1, fixture.TokenCalls);
    }

    [Theory]
    [InlineData("owner", "different")]
    [InlineData("repo", "different")]
    [InlineData("method", "merge")]
    [InlineData("pullNumber", 0)]
    [InlineData("perPage", 101)]
    [InlineData("confirmed", true)]
    public void Native_read_policy_rejects_cross_repository_writes_unknown_fields_and_unbounded_pages(string field, object value)
    {
        var arguments = ReadArguments(); arguments[field] = value;
        Assert.Throws<McpOperationException>(() => GitHubRepositoryTools.ValidateArguments(Binding(), "pull_request_read", arguments));
    }

    [Fact]
    public async Task Snapshot_uses_current_merge_checks_and_rejects_an_old_green_attempt()
    {
        using var fixture = new Fixture { MergeSha = Merge, MergeRunState = "in_progress", MergeConclusion = null };
        using var actor = VerifiedActor.Enter(Actor);
        var snapshot = await fixture.Source.GetPullRequestAsync(fixture.Binding, 12, TestContext.Current.CancellationToken);
        Assert.True(snapshot.ChecksComplete);
        Assert.Equal(Merge, snapshot.CiSha);
        Assert.Equal("in_progress", Assert.Single(snapshot.Checks).State);
        Assert.Null(Assert.Single(snapshot.Checks).Conclusion);
        Assert.Equal(42, snapshot.RepositoryId);
        fixture.MergeRunState = "completed"; fixture.MergeConclusion = "success";
        var green = await fixture.Source.GetPullRequestAsync(fixture.Binding, 12, TestContext.Current.CancellationToken);
        Assert.Equal(snapshot.Revision, green.Revision);
        Assert.NotEqual(snapshot.CiRevision, green.CiRevision);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task Stale_merge_and_incomplete_catalog_do_not_count_as_complete(bool staleMerge, bool incomplete, bool suitePending)
    {
        using var fixture = new Fixture { MergeSha = staleMerge ? Merge : null, StaleMerge = staleMerge, Incomplete = incomplete, SuitePending = suitePending };
        using var actor = VerifiedActor.Enter(Actor);
        var snapshot = await fixture.Source.GetPullRequestAsync(fixture.Binding, 12, TestContext.Current.CancellationToken);
        Assert.False(snapshot.ChecksComplete);
    }

    [Fact]
    public async Task Rerequested_suite_overrides_old_successful_check_until_fresh_jobs_appear()
    {
        using var fixture = new Fixture { SuitePending = true };
        using var actor = VerifiedActor.Enter(Actor);
        var snapshot = await fixture.Source.GetPullRequestAsync(fixture.Binding, 12, TestContext.Current.CancellationToken);
        Assert.True(snapshot.ChecksComplete);
        Assert.Equal("queued", Assert.Single(snapshot.Checks).State);
        Assert.Null(Assert.Single(snapshot.Checks).Conclusion);
    }

    [Fact]
    public async Task Commit_statuses_use_latest_context_and_are_not_confused_with_check_app_identity()
    {
        using var fixture = new Fixture { IncludeStatuses = true };
        using var actor = VerifiedActor.Enter(Actor);
        var snapshot = await fixture.Source.GetPullRequestAsync(fixture.Binding, 12, TestContext.Current.CancellationToken);
        var status = Assert.Single(snapshot.Checks, check => check.Kind == "status");
        Assert.Null(status.AppId);
        Assert.Equal("pending", status.State);
        Assert.Equal("9", status.AttemptId);
    }

    [Fact]
    public async Task Pagination_stays_on_fixed_repository_even_when_next_link_names_another_host()
    {
        using var fixture = new Fixture { TwoCheckPages = true };
        using var actor = VerifiedActor.Enter(Actor);
        var snapshot = await fixture.Source.GetPullRequestAsync(fixture.Binding, 12, TestContext.Current.CancellationToken);
        Assert.True(snapshot.ChecksComplete);
        Assert.Equal(2, snapshot.Checks.Length);
        Assert.Contains(snapshot.Checks, check => check.Name == "quality");
    }

    [Fact]
    public async Task Only_a_known_get_401_refreshes_and_replays_once()
    {
        using var fixture = new Fixture { UnauthorizedOnce = true };
        using var actor = VerifiedActor.Enter(Actor);
        var snapshot = await fixture.Source.GetPullRequestAsync(fixture.Binding, 12, TestContext.Current.CancellationToken);
        Assert.True(snapshot.ChecksComplete);
        Assert.Equal(2, fixture.TokenCalls);
    }

    [Fact]
    public async Task Oversized_patches_block_complete_review_without_truncation_claims()
    {
        using var fixture = new Fixture { OversizedPatch = true };
        using var actor = VerifiedActor.Enter(Actor);
        var snapshot = await fixture.Source.GetPullRequestAsync(fixture.Binding, 12, TestContext.Current.CancellationToken);
        var evidence = await fixture.Source.GetReviewEvidenceAsync(fixture.Binding, snapshot, TestContext.Current.CancellationToken);
        Assert.False(evidence.Complete);
        Assert.True(Encoding.UTF8.GetByteCount(evidence.Text) < GitHubRepositorySource.EvidenceBudgetBytes);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task Review_evidence_is_pinned_complete_and_hashes_exact_content(bool missingPatch, bool moves, bool complete)
    {
        using var fixture = new Fixture { MissingPatch = missingPatch };
        using var actor = VerifiedActor.Enter(Actor);
        var snapshot = await fixture.Source.GetPullRequestAsync(fixture.Binding, 12, TestContext.Current.CancellationToken);
        fixture.MoveDuringFiles = moves;
        var evidence = await fixture.Source.GetReviewEvidenceAsync(fixture.Binding, snapshot, TestContext.Current.CancellationToken);
        Assert.Equal(complete, evidence.Complete);
        Assert.Equal(Head, evidence.HeadSha); Assert.Equal(Base, evidence.BaseSha);
        Assert.Equal(GitHubRepositorySource.Hash(evidence.Text), evidence.Hash);
        Assert.True(Encoding.UTF8.GetByteCount(evidence.Text) <= GitHubRepositorySource.EvidenceBudgetBytes);
        if (complete)
        {
            Assert.Contains("src/example.cs", evidence.Text, StringComparison.Ordinal);
            Assert.Contains("+new", evidence.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Repository_rename_revokes_access_before_any_native_call()
    {
        using var fixture = new Fixture { RepositoryId = 999 };
        using var actor = VerifiedActor.Enter(Actor);
        var error = await Assert.ThrowsAsync<McpOperationException>(() => fixture.Tokens.VerifyRepositoryAsync(fixture.Binding, TestContext.Current.CancellationToken));
        Assert.Equal(McpFailureKind.ConnectionChanged, error.Kind);
        Assert.False(fixture.Binding.Enabled);
    }

    [Fact]
    public async Task Native_catalog_keeps_server_schemas_and_checks_actor_again_on_invoke()
    {
        using var fixture = new Fixture();
        var calls = 0;
        await using var server = new McpDiscoveredToolTests.FakeMcpServer
        {
            Tools = [Definition("pull_request_read"), Definition("list_pull_requests"), Definition("get_file_contents"), Definition("merge_pull_request")],
            OnToolCall = _ => { calls++; return new CallToolResult { Content = [], StructuredContent = JsonSerializer.SerializeToElement(new { observed = true }) }; },
        };
        var client = new McpDiscoveredToolClient<GitHubRepositoryTools.GitHubAgentIdentity>(new McpStdioConnection
        {
            Name = "github-fixture", Command = "unused", AllowedToolNames = GitHubRepositoryTools.NativeTools,
        }, null, (_, cancellationToken) => server.ConnectAsync("github", cancellationToken));
        await using var tools = new GitHubRepositoryTools(fixture.Binding, fixture.Tokens, new AllowScreen(), client);
        using var actor = VerifiedActor.Enter(Actor);
        using var context = new AgentToolContext(new NeuronId("repository", Owner, fixture.Binding.InstanceName), Actor.PrincipalId, new NoRequests());
        var prepared = await tools.GetToolsAsync(context, TestContext.Current.CancellationToken);
        var read = Assert.Single(prepared.OfType<AIFunction>(), tool => tool.Name == "pull_request_read");
        Assert.True(JsonElement.DeepEquals(server.Tools[0].InputSchema, read.JsonSchema));
        Assert.DoesNotContain(prepared, tool => tool.Name == "merge_pull_request");
        _ = await read.InvokeAsync(new AIFunctionArguments(ReadArguments()), TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
        using var other = VerifiedActor.Enter(new ActorContext(PrincipalId.New(), "other"));
        await Assert.ThrowsAsync<McpOperationException>(() => read.InvokeAsync(new AIFunctionArguments(ReadArguments()), TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(1, calls);
    }

    private static Dictionary<string, object?> ReadArguments() => new() { ["owner"] = "acme", ["repo"] = "brain", ["method"] = "get", ["pullNumber"] = 12 };
    private static GitHubRepositoryBinding Binding(string? privateKey = null) => new("source", Owner, Actor.PrincipalId, 42, 6, 7, "acme", "brain", privateKey ?? Key, "fixture-webhook-secret");
    private static byte[] Decode(string text) => Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4));
    private static Tool Definition(string name) => new()
    {
        Name = name, Description = "Native GitHub tool", InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object", properties = new { owner = new { type = "string" }, repo = new { type = "string" }, method = new { type = "string" }, pullNumber = new { type = "number" } },
            required = new[] { "owner", "repo" },
        }),
    };
    private sealed class AllowScreen : IUntrustedContentScreen
    {
        public Task ScreenAsync(string content, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class NoRequests : IAgentRequests
    {
        public Task<AgentReply> RequestAsync<TAgent>(string instanceName, AgentRequest request, CancellationToken cancellationToken = default)
            where TAgent : IAgent => throw new NotSupportedException();
    }
    private sealed class CapturingRequests : IAgentRequests
    {
        internal string? Instance;
        internal AgentRequest? Request;
        internal int Calls;
        public Task<AgentReply> RequestAsync<TAgent>(string instanceName, AgentRequest request, CancellationToken cancellationToken = default) where TAgent : IAgent
        {
            Instance = instanceName; Request = request; Calls++;
            return Task.FromResult(new AgentReply("Observed repository evidence"));
        }
    }

    private sealed class Fixture : IDisposable
    {
        public GitHubRepositoryBinding Binding { get; } = GitHubConnectionTests.Binding();
        public GitHubInstallationTokens Tokens { get; }
        public GitHubRepositorySource Source { get; }
        public int TokenCalls;
        public JsonElement? TokenRequest;
        public bool TokenWritePermission;
        public string? MergeSha;
        public string MergeRunState = "completed";
        public string? MergeConclusion = "success";
        public bool StaleMerge;
        public bool Incomplete;
        public bool SuitePending;
        public bool IncludeStatuses;
        public bool MissingPatch;
        public bool MoveDuringFiles;
        public bool OversizedPatch;
        public bool TwoCheckPages;
        public bool UnauthorizedOnce;
        public long RepositoryId = 42;
        private bool _moved;
        public Fixture()
        {
            Tokens = new GitHubInstallationTokens(new Handler(RespondAsync), null);
            Source = new GitHubRepositorySource(Tokens, new Handler(RespondAsync), null);
        }

        private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.Equal("api.github.com", request.RequestUri.Host);
            if (path == "/app/installations/6/access_tokens")
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                TokenCalls++;
                TokenRequest = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token)).RootElement.Clone();
                return Json(new { token = "installation-token", expires_at = DateTimeOffset.UtcNow.AddHours(1), repositories = new[] { new { id = 42 } },
                    permissions = new { contents = TokenWritePermission ? "write" : "read", pull_requests = "read", checks = "read", statuses = "read", metadata = "read" } });
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("installation-token", request.Headers.Authorization?.Parameter);
            if (path == "/repos/acme/brain")
            {
                return Json(new { id = RepositoryId, name = "brain", owner = new { login = "acme" } });
            }
            if (path == "/repos/acme/brain/pulls/12")
            {
                if (UnauthorizedOnce)
                {
                    UnauthorizedOnce = false;
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }
                return Json(new { number = 12, title = "Test PR", html_url = "https://github.com/acme/brain/pull/12", state = "open", draft = false,
                    head = new { sha = _moved ? new string('d', 40) : Head }, @base = new { sha = Base, repo = new { id = RepositoryId, name = "brain", owner = new { login = "acme" } } },
                    merge_commit_sha = MergeSha, created_at = "2026-09-05T10:00:00Z", changed_files = 1 });
            }
            if (path.EndsWith("/check-runs", StringComparison.Ordinal))
            {
                var merge = path.Contains(Merge, StringComparison.Ordinal);
                var second = request.RequestUri.Query.EndsWith("page=2", StringComparison.Ordinal);
                var run = new { id = merge || second ? 2 : 1, name = second ? "quality" : "build", app = new { id = 15368 }, check_suite = new { id = 1 }, head_sha = merge ? Merge : Head,
                    status = merge ? MergeRunState : "completed", conclusion = merge ? MergeConclusion : "success", started_at = "2026-09-05T10:00:00Z", completed_at = (string?)null };
                var response = Json(new { total_count = Incomplete || TwoCheckPages ? 2 : 1, check_runs = new[] { run } });
                if (TwoCheckPages && !second)
                {
                    response.Headers.Add("Link", "<https://another-host.invalid/steal>; rel=\"next\"");
                }
                return response;
            }
            if (path.EndsWith("/check-suites", StringComparison.Ordinal))
            {
                return Json(new { total_count = 1, check_suites = new[] { new { id = 1, status = SuitePending ? "queued" : "completed" } } });
            }
            if (path.EndsWith("/statuses", StringComparison.Ordinal))
            {
                return Json(IncludeStatuses ? new[] { new { id = 9, context = "legacy-ci", state = "pending", updated_at = "2026-09-05T10:10:00Z" },
                    new { id = 8, context = "legacy-ci", state = "success", updated_at = "2026-09-05T10:00:00Z" } } : []);
            }
            if (path.Contains("/git/commits/", StringComparison.Ordinal))
            {
                return Json(new { parents = new[] { new { sha = StaleMerge ? new string('e', 40) : Head }, new { sha = Base } } });
            }
            if (path.EndsWith("/files", StringComparison.Ordinal))
            {
                _moved = MoveDuringFiles;
                return Json(new[] { new { filename = "src/example.cs", sha = new string('f', 40), status = "modified", additions = 1, deletions = 1,
                    patch = MissingPatch ? null : "@@ -1 +1 @@\n-old\n+new" + (OversizedPatch ? "\n " + new string('x', GitHubRepositorySource.EvidenceBudgetBytes) : string.Empty) } });
            }
            throw new InvalidOperationException($"Unexpected fixture request {path}");
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
        public void Dispose() { Source.Dispose(); Tokens.Dispose(); }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
