using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;
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

    private SalesforceQueryResult? _salesforceRead;
    private SalesforceUnavailableException? _salesforceError;

    private ISalesforce Salesforce => world.Brain.Grains.GetGrain<ISalesforce>(new NeuronId("salesforce", "salesforce").ToGrainId());

    [Given("a running brain with the Salesforce module in fake mode")]
    public async Task StartSalesforce()
        => world.Simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(SalesforceModule)]),
            Configuration = new Dictionary<string, string?>
            {
                ["DigitalBrain:Fakes:Enabled"] = "true",
                ["DigitalBrain:Salesforce:OAuth:ConsumerKey"] = "fake-consumer-key",
                ["DigitalBrain:Salesforce:OAuth:ConsumerSecret"] = "fake-consumer-secret",
                ["DigitalBrain:Salesforce:OAuth:PublicOrigin"] = "http://localhost:5080",
            },
        });

    [When("the Salesforce account connects")]
    public async Task ConnectSalesforce()
        => await Salesforce.Connect(new ConnectSalesforceAccount(CommandId.New(), "fake-access-token", null, 3600,
            "https://fixture.my.salesforce.com"));

    [Then("the Salesforce connection is reported")]
    public async Task SalesforceConnection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        SalesforceConnection connection;
        do
        {
            connection = await Salesforce.ReadConnection().WaitAsync(timeout.Token);
            if (!connection.Connected)
            {
                await Task.Delay(20, timeout.Token);
            }
        } while (!connection.Connected);
        Assert.Equal("https://fixture.my.salesforce.com", connection.InstanceUrl);
        Assert.True(connection.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [When("the guarded query {string} runs")]
    public async Task QuerySalesforce(string query)
    {
        _salesforceRead = null;
        _salesforceError = null;
        try { _salesforceRead = await Salesforce.Query(new(query)); }
        catch (SalesforceUnavailableException error) { _salesforceError = error; }
    }

    [Then("the Salesforce query returns {int} records")]
    public void SalesforceRecords(int count)
    {
        Assert.Null(_salesforceError);
        Assert.NotNull(_salesforceRead);
        Assert.Equal(count, _salesforceRead.TotalSize);
        Assert.Equal(count, _salesforceRead.Records.GetArrayLength());
    }

    [Then("the Salesforce query was refused")]
    public void SalesforceRefused()
    {
        Assert.Null(_salesforceRead);
        Assert.NotNull(_salesforceError);
        Assert.Equal("Use one SELECT with an outer WHERE and positive LIMIT. Comments, multiple statements and locking queries are not allowed.",
            _salesforceError.Message);
    }
}
