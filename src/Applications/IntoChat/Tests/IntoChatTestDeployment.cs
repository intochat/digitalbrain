using DigitalBrain.AI;
using DigitalBrain.AI.OpenAI;
using DigitalBrain.ClickHouse;
using DigitalBrain.Coding;
using DigitalBrain.Flutter;
using DigitalBrain.Google;
using DigitalBrain.Memory;
using DigitalBrain.Microsoft;
using DigitalBrain.Salesforce;
using DigitalBrain.Supabase;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IntoChat.Tests;

/// <summary>The actual application graph with run-scoped resources and local protocol fixtures.</summary>
public sealed class IntoChatTestDeployment : IAsyncDisposable
{
    private readonly WebApplication _fixtures;
    private readonly string _directory;
    private IntoChatTestDeployment(WebApplication fixtures, string directory)
    {
        _fixtures = fixtures;
        _directory = directory;
    }
    public Uri Endpoint => new(_fixtures.Urls.Single());
    public string SolutionPath => Path.Combine(_directory, "Fixture.slnx");

    public static async Task<IntoChatTestDeployment> CreateAsync(CancellationToken ct)
    {
        var directory = Path.Combine(Path.GetTempPath(), "intochat-deployment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "Fixture.slnx"), "<Solution />", ct);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapPost("/token", () => Results.Json(new { access_token = "fixture-access", refresh_token = "fixture-refresh", token_type = "Bearer", expires_in = 3600 }));
        app.MapPost("/mcp", () => Results.Json(new { jsonrpc = "2.0", id = 1, result = new { tools = Array.Empty<object>() } }));
        // Non-agent tests must never silently contact a live model.
        app.MapPost("/v1/{**path}", () => Results.Problem("This scenario has not configured model responses.", statusCode: 501));
        await app.StartAsync(ct);
        return new(app, directory);
    }

    public E2ETestBuilder<Projects.IntoChat_AppHost> Configure(
        E2ETestBuilder<Projects.IntoChat_AppHost> test, Uri modelEndpoint, string solutionPath)
        => test
            .ConfigureModule<AIModule>(ai => ai.WithoutLocalModels().WithoutVoiceToText().WithoutWebSearch()
                .WithDefaultLlm<IGpt56Luna>().WithModelEndpoint(AiProvider.OpenAI, modelEndpoint))
            .ConfigureModule<MemoryModule>(memory => memory.WithQdrant())
            .ConfigureModule<ClickHouseModule>(database => database.WithClickHouse(o => { o.PersistentStorage = false; o.WithSeed("leads"); }))
            .ConfigureModule<SupabaseModule>(database => database.WithPostgres())
            .ConfigureModule<GoogleModule>(google => google.WithTokenEndpoint(new Uri(Endpoint, "/token")))
            .ConfigureModule<SalesforceModule>(salesforce => salesforce.WithLocalMcp(new Uri(Endpoint, "/mcp")))
            .ConfigureModule<MicrosoftModule>(microsoft => microsoft.WithoutAspire().WithGitHubRepositories(new Dictionary<string, GitHubRepositoryDeclaration>()))
            .ConfigureModule<CodingModule>(coding => coding.WithSolution(solutionPath))
            .ConfigureModule<FlutterModule>(flutter => flutter.WithWebHost())
            .WithExecution(new()
            {
                PrivateConfiguration = new Dictionary<string, string?>
                {
                    ["Parameters:openai-api-key"] = "fixture-key",
                    ["Parameters:gmail-client-id"] = "fixture-client",
                    ["Parameters:gmail-client-secret"] = "fixture-secret",
                    ["Parameters:salesforce-consumer-key"] = "fixture-client",
                    ["Parameters:salesforce-consumer-secret"] = "fixture-secret",
                },
            });

    public E2ETestBuilder<Projects.IntoChat_AppHost> CreateTest(bool web = false)
    {
        var test = Configure(E2ETest.For<Projects.IntoChat_AppHost>(), new Uri(Endpoint, "/v1"), SolutionPath);
        return web ? test : test.ConfigureModule<FlutterModule>(flutter => flutter.WithoutHost());
    }

    public async ValueTask DisposeAsync()
    {
        await _fixtures.DisposeAsync();
        // Only the unique directory created and owned by this fixture is removed.
        if (Directory.Exists(_directory)) { Directory.Delete(_directory, recursive: true); }
    }
}
