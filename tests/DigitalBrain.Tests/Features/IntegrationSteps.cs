using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Microsoft;
using DigitalBrain.Microsoft.GitHub;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class IntegrationSteps(BrainWorld world)
{
    private byte[]? _githubBody;
    private Dictionary<string, string[]>? _githubHeaders;
    private GitHubWebhookAcceptance _githubAcceptance;

    [When("a GitHub connection mismatches its authorized repository binding")]
    public async Task MismatchedGitHubConnection()
    {
        var id = new NeuronId("repository", "fake");
        var binding = world.Brain.SiloServices.GetRequiredService<GitHubRepositoryBindings>().GetFor(id);
        var repository = world.Brain.Grains.GetGrain<IRepository>(id.ToGrainId());
        await repository.Connect(new(CommandId.New(), binding.AppId, binding.InstallationId,
            binding.RepositoryId + 1, binding.RepoOwner, binding.RepoName));
    }

    [Given("a running brain with the Microsoft module in fake mode")]
    public async Task StartMicrosoft()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(MicrosoftModule)]),
            Configuration = new Dictionary<string, string?> { ["DigitalBrain:Fakes:Enabled"] = "true" },
        });

    [When("a signed {string} webhook delivery for pull request {int} arrives")]
    public async Task GitHubDelivery(string eventName, int number)
    {
        _githubBody = JsonSerializer.SerializeToUtf8Bytes(new
        {
            action = "opened",
            number,
            installation = new { id = 2 },
            repository = new { id = 1, name = "repository", owner = new { login = "fixture" } },
        });
        _githubHeaders = new()
        {
            ["X-GitHub-Delivery"] = [Guid.NewGuid().ToString()],
            ["X-GitHub-Event"] = [eventName],
            ["X-Hub-Signature-256"] = ["sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(FakeGitHubRepositorySource.WebhookSecret), _githubBody))],
        };
        await RepeatGitHubDelivery();
        Assert.Equal(GitHubWebhookAcceptance.Accepted, _githubAcceptance);
    }

    [Then("the repository reports pull request {int} as open")]
    public async Task GitHubPullRequest(int number)
    {
        var repository = world.Brain.Grains.GetGrain<IRepository>(new NeuronId("repository", "fake").ToGrainId());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        RepositoryView? view = null;
        await ReactionWait.UntilAsync(async () =>
        {
            view = await repository.Read().WaitAsync(timeout.Token);
            return view.PullRequests.Any(item => item.Number == number);
        }, timeout.Token);
        Assert.NotNull(view);
        Assert.True(Assert.Single(view.PullRequests, item => item.Number == number).IsOpen);
        Assert.NotNull(view.LastWebhookAt);
        var read = await world.Brain.SiloServices.GetRequiredService<GitHubNativeTools>()
            .ReadPullRequest("fake", new(number), timeout.Token);
        Assert.Equal(number, read.Snapshot.Number);
    }

    [When("the same signed webhook delivery arrives again")]
    public async Task RepeatGitHubDelivery()
    {
        Assert.NotNull(_githubBody);
        Assert.NotNull(_githubHeaders);
        _githubAcceptance = await world.Brain.SiloServices.GetRequiredService<GitHubWebhookIngress>()
            .HandleAsync(_githubBody, _githubHeaders, CancellationToken.None);
    }

    [Then("the repository accepted it as a duplicate")]
    public void GitHubDuplicate() => Assert.Equal(GitHubWebhookAcceptance.Duplicate, _githubAcceptance);
}
