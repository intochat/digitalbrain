using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Google;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class IntegrationSteps(BrainWorld world)
{
    private GmailContentRead? _read;

    private IGmail Gmail => world.Brain.Grains.GetGrain<IGmail>(new NeuronId("gmail", "gmail").ToGrainId());

    [Given("a running brain with the Google module in fake mode")]
    public async Task Start()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(GoogleModule)]),
            Configuration = new Dictionary<string, string?> { ["DigitalBrain:Fakes:Enabled"] = "true" },
        });

    [When("the Gmail account {string} connects")]
    public async Task Connect(string email)
    {
        await Gmail.Connect(new ConnectGmailAccount(CommandId.New(), "google-subject", email,
            "fake-access-token", null, "openid email https://www.googleapis.com/auth/gmail.readonly", 3600));
    }

    [Then("the Gmail connection reports {string}")]
    public async Task Connection(string email)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        GmailConnection connection;
        do
        {
            connection = await Gmail.ReadConnection().WaitAsync(timeout.Token);
            if (!connection.Connected)
            {
                await Task.Delay(20, timeout.Token);
            }
        } while (!connection.Connected);
        Assert.Equal(email, connection.Email);
    }

    [When("the {string} Gmail tool runs for {string}")]
    public async Task Search(string tool, string query)
    {
        Assert.Equal("search_threads", tool);
        _read = await world.Brain.SiloServices.GetRequiredService<GmailNativeTools>().SearchThreads(new(query));
    }

    [Then("the Gmail read returns thread {string}")]
    public void Thread(string id)
    {
        Assert.NotNull(_read);
        Assert.Equal(id, _read.Content.GetProperty("threads")[0].GetProperty("id").GetString());
    }
}
