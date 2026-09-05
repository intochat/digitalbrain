using System.Diagnostics;
using System.Text.Json;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Product.Interactions;
using DigitalBrain.Sdk;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace DigitalBrain.Microsoft.GitHub;

internal sealed class GitHubRepositoryConnections : IAsyncDisposable
{
    private readonly IReadOnlyDictionary<string, GitHubRepositoryTools> _sources;
    public GitHubRepositoryConnections(GitHubRepositoryBindings bindings, GitHubInstallationTokens tokens, IUntrustedContentScreen screen)
        => _sources = bindings.All.ToDictionary(static binding => binding.Id,
            binding => new GitHubRepositoryTools(binding, tokens, screen), StringComparer.Ordinal);
    public IAgentToolSource For(GitHubRepositoryBinding binding)
        => _sources.TryGetValue(binding.Id, out var source) ? source : throw new McpOperationException("The GitHub repository is not configured.");
    public async ValueTask DisposeAsync()
    {
        foreach (var source in _sources.Values)
        {
            await source.DisposeAsync().ConfigureAwait(false);
        }
    }
}

internal sealed class GitHubRepositoryTools : IAgentToolSource, IAsyncDisposable
{
    internal static readonly string[] NativeTools = ["pull_request_read", "list_pull_requests", "get_file_contents"];
    internal static readonly string[] ReadMethods = ["get", "get_diff", "get_status", "get_files", "get_commits", "get_review_comments", "get_reviews", "get_comments", "get_check_runs"];
    private readonly GitHubRepositoryBinding _binding;
    private readonly GitHubInstallationTokens _tokens;
    private readonly IUntrustedContentScreen _screen;
    private readonly McpDiscoveredToolClient<GitHubAgentIdentity> _client;

    internal GitHubRepositoryTools(GitHubRepositoryBinding binding, GitHubInstallationTokens tokens, IUntrustedContentScreen screen,
        McpDiscoveredToolClient<GitHubAgentIdentity>? client = null)
    {
        _binding = binding; _tokens = tokens; _screen = screen;
        _client = client ?? McpDiscoveredToolClient<GitHubAgentIdentity>.ForHttp(new McpEndpoint("github", binding.McpEndpoint),
            new Credentials(binding, tokens), static identity => identity.Agent.Owner, Authorize, NativeTools,
            new McpToolPolicy(static _ => true, ValidateCatalog),
            new McpSessionOptions { Capacity = 4, Timeout = TimeSpan.FromSeconds(30), ResponseBudgetBytes = 262144, Lifetime = TimeSpan.FromMinutes(10) });
    }

    public async ValueTask<IReadOnlyList<AITool>> GetToolsAsync(AgentToolContext context, CancellationToken cancellationToken)
    {
        var identity = RequireIdentity(context);
        var operation = Guid.NewGuid();
        var started = Stopwatch.GetTimestamp();
        try
        {
            await _tokens.VerifyRepositoryAsync(_binding, cancellationToken).ConfigureAwait(true);
            var native = await _client.GetToolsAsync(identity, cancellationToken).ConfigureAwait(true);
            await context.ObserveAsync(new AgentActivity(operation, "tool", "completed", "tools/list", Server: "github",
                DurationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                Preview: McpEvidencePreview.Create(string.Join(", ", native.Select(static tool => tool.Name))))).ConfigureAwait(true);
            return native.Select(tool => (AITool)AgentToolExecution.Observe(context,
                new PolicyTool(tool, this, context), "github", _screen)).ToArray();
        }
        catch (Exception error)
        {
            await context.ObserveAsync(new AgentActivity(operation, "tool", "failed", "tools/list", Server: "github", IsError: true,
                DurationMs: Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                FailureCode: error is McpOperationException { Kind: McpFailureKind.AccessDenied } ? "access_denied" : "unavailable")).ConfigureAwait(true);
            throw;
        }
    }

    private GitHubAgentIdentity RequireIdentity(AgentToolContext context)
    {
        context.RequireActive();
        if (context.Principal is not { } principal || context.Agent.Type != "repository"
            || context.Agent.Name != _binding.InstanceName || context.Agent.Owner != _binding.Owner)
        {
            throw new McpOperationException("GitHub tools belong to the configured repository specialist.", McpFailureKind.AccessDenied);
        }
        _binding.Authorize(context.Owner, principal);
        return new GitHubAgentIdentity(context.Agent, principal, _binding.Revision);
    }

    private static void Authorize(GitHubAgentIdentity identity, GitHubRepositoryBinding binding)
    {
        binding.Authorize(identity.Agent.Owner, identity.Principal);
        if (identity.Agent.Type != "repository" || identity.Agent.Name != binding.InstanceName || identity.Revision != binding.Revision)
        {
            throw new McpOperationException("The GitHub repository connection changed. Request fresh tools.", McpFailureKind.ConnectionChanged);
        }
    }

    internal static void ValidateArguments(GitHubRepositoryBinding binding, string name, IReadOnlyDictionary<string, object?> arguments)
    {
        var json = JsonSerializer.SerializeToElement(arguments);
        if (!NativeTools.Contains(name, StringComparer.Ordinal)
            || !string.Equals(String(json, "owner"), binding.RepoOwner, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(String(json, "repo"), binding.RepoName, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpOperationException("Only read operations on the configured GitHub repository are admitted.", McpFailureKind.AccessDenied);
        }
        string[] fields = name switch
        {
            "pull_request_read" => ["owner", "repo", "method", "pullNumber", "page", "perPage", "after"],
            "list_pull_requests" => ["owner", "repo", "base", "direction", "fields", "head", "page", "perPage", "sort", "state"],
            _ => ["owner", "repo", "path", "ref", "sha", "fields"],
        };
        if (arguments.Keys.Any(key => !fields.Contains(key, StringComparer.Ordinal)) || JsonSerializer.Serialize(arguments).Length > 8192)
        {
            throw new McpOperationException("The GitHub arguments exceed the admitted native read schema.", McpFailureKind.AccessDenied);
        }
        foreach (var property in json.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String
                && (property.Value.GetString()!.Length > 4096 || property.Value.GetString()!.Any(char.IsControl)))
            {
                throw new McpOperationException("GitHub read arguments must be bounded plain values.", McpFailureKind.AccessDenied);
            }
        }
        if (name == "pull_request_read" && (!ReadMethods.Contains(String(json, "method"), StringComparer.Ordinal)
            || !PositiveNumber(json, "pullNumber", int.MaxValue)))
        {
            throw new McpOperationException("This GitHub PR method or number is outside the admitted read operations.", McpFailureKind.AccessDenied);
        }
        if (json.TryGetProperty("page", out _) && !PositiveNumber(json, "page", 100)
            || json.TryGetProperty("perPage", out _) && !PositiveNumber(json, "perPage", 100))
        {
            throw new McpOperationException("GitHub pagination must stay within 100 pages and 100 results per page.", McpFailureKind.Capacity);
        }
        if (name == "get_file_contents")
        {
            var path = String(json, "path");
            if (path is not null && (path.StartsWith('/') || path.Contains('\\') || path.Contains('%') || path.Contains(':') || path.Split('/').Any(static segment => segment is "." or "..")))
            {
                throw new McpOperationException("GitHub file reads require a relative repository path.", McpFailureKind.AccessDenied);
            }
            if (String(json, "sha") is { } sha && !GitHubRepositorySource.IsSha(sha))
            {
                throw new McpOperationException("GitHub commit reads require a complete commit SHA.", McpFailureKind.AccessDenied);
            }
        }
    }

    private static void ValidateCatalog(IEnumerable<McpClientTool> tools)
    {
        var catalog = tools.ToDictionary(static tool => tool.Name, StringComparer.Ordinal);
        foreach (var name in NativeTools)
        {
            if (!catalog.TryGetValue(name, out var tool) || !tool.JsonSchema.TryGetProperty("properties", out var properties)
                || !StringProperty(properties, "owner") || !StringProperty(properties, "repo"))
            {
                throw new McpOperationException("The GitHub MCP catalog is incompatible with repository-bound native reads.", McpFailureKind.CatalogChanged);
            }
            if (name == "pull_request_read" && (!StringProperty(properties, "method") || !properties.TryGetProperty("pullNumber", out var number)
                || String(number, "type") is not "number" and not "integer"))
            {
                throw new McpOperationException("The GitHub MCP pull-request read schema changed.", McpFailureKind.CatalogChanged);
            }
        }
    }

    private static bool StringProperty(JsonElement properties, string name)
        => properties.TryGetProperty(name, out var property) && String(property, "type") == "string";
    private static string? String(JsonElement json, string name)
        => json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool PositiveNumber(JsonElement json, string name, int maximum)
        => json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            && number >= 1 && number <= maximum && Math.Truncate(number) == number;
    public ValueTask DisposeAsync() => _client.DisposeAsync();

    internal sealed record GitHubAgentIdentity(NeuronId Agent, PrincipalId Principal, string Revision);
    private sealed class Credentials(GitHubRepositoryBinding binding, GitHubInstallationTokens tokens) : IMcpCredentials<GitHubRepositoryBinding>
    {
        public GitHubRepositoryBinding Connection(OwnerId owner)
        {
            binding.Authorize(owner, VerifiedActor.Current?.PrincipalId ?? default);
            return binding;
        }
        public Task<string> AccessTokenAsync(OwnerId owner, GitHubRepositoryBinding connection, bool refresh, CancellationToken cancellationToken)
        {
            if (!ReferenceEquals(binding, connection))
            {
                throw new McpOperationException("The GitHub repository connection changed.", McpFailureKind.ConnectionChanged);
            }
            binding.Authorize(owner, VerifiedActor.Current?.PrincipalId ?? default);
            return tokens.GetTokenAsync(binding, refresh, cancellationToken);
        }
        public Task RejectAsync(OwnerId owner, GitHubRepositoryBinding connection, CancellationToken cancellationToken)
        {
            binding.Authorize(owner, VerifiedActor.Current?.PrincipalId ?? default);
            return Task.CompletedTask; // A later turn can obtain a new installation token; never broaden permissions.
        }
    }

    private sealed class PolicyTool(AIFunction native, GitHubRepositoryTools source, AgentToolContext context) : DelegatingAIFunction(native)
    {
        public override string Description => base.Description + $" Application scope: read-only {source._binding.RepoOwner}/{source._binding.RepoName}. Use that exact owner and repo.";
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            source.RequireIdentity(context);
            ValidateArguments(source._binding, Name, new Dictionary<string, object?>(arguments));
            await source._tokens.VerifyRepositoryAsync(source._binding, cancellationToken).ConfigureAwait(true);
            var result = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(true);
            source.RequireIdentity(context);
            if (McpDiscoveredTool.IsError(result) || McpDiscoveredTool.IsTruncated(result))
            {
                throw new McpOperationException("GitHub MCP did not return complete successful evidence. Narrow the read or verify access.", McpFailureKind.ContentRejected);
            }
            return result;
        }
    }
}
