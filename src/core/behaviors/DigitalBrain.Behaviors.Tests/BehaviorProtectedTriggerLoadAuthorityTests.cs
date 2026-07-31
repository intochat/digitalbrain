using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorProtectedTriggerLoadAuthorityTests(BehaviorDispatchFixture fixture)
{
    private const string ValidCredential = "trigger-load-authority-credential";
    private const string SecretTrigger = """{"Label":"trigger-secret-must-not-leak"}""";

    [Fact(
        Timeout = 120_000,
        DisplayName =
            "trigger load requires active Task/attempt/Worker/activation authority; forged cases refuse without plaintext")]
    public async Task LoadRequiresActiveTaskAuthorityAndRefusesForgedIdentities()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var worker = brain.Neuron<IWorker>("trigger-auth-worker");
        var task = brain.Neuron<ITask>("trigger-auth-task");
        var behavior = new BehaviorId(BehaviorsFixture.SampleBehavior);
        var revision = new BehaviorRevisionId(
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        const string caseId = "case.SampleTrigger";

        var triggers = new GrainBehaviorProtectedTriggerAccess(brain.Cluster.Client);
        var reference = await triggers.StoreAsync(
            task.Id.Owner,
            task.Id,
            behavior,
            revision,
            caseId,
            Encoding.UTF8.GetBytes(SecretTrigger),
            cancellationToken);

        var activation = new BehaviorTaskActivation(
            behavior,
            revision,
            contractVersion: "1",
            caseId: caseId,
            protectedPayload: reference,
            triggerTypeName: "SampleTrigger",
            capabilities: []);
        var goal = new BehaviorActivationGoal(
            activation.BehaviorId,
            activation.Revision,
            activation.ContractVersion,
            activation.CaseId,
            activation.ProtectedPayload,
            activation.TriggerTypeName,
            activation.Capabilities);

        var started = await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation))
            .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        Assert.NotNull(started.ActiveAttempt);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        TaskSnapshot running;
        do
        {
            running = await task.Reference.Read();
            if (running.State == TaskState.Running)
            {
                break;
            }

            await Task.Delay(25, cancellationToken);
        }
        while (DateTime.UtcNow < deadline);
        Assert.Equal(TaskState.Running, running.State);

        await using var host = await StartHostAsync(brain, cancellationToken);
        using var client = host.CreateClient();

        using var ok = await PostLoadAsync(
            client,
            task.Id.Owner.Value,
            task.Id,
            started.ActiveAttempt!.Value,
            worker.Id,
            behavior,
            revision,
            caseId,
            reference,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var loaded = await ok.Content.ReadFromJsonAsync<LoadTriggerResponse>(cancellationToken: cancellationToken);
        Assert.NotNull(loaded);
        Assert.Equal(SecretTrigger, Encoding.UTF8.GetString(Convert.FromBase64String(loaded.ContentBase64)));

        using var forgedOwner = await PostLoadAsync(
            client,
            "forged-owner",
            task.Id,
            started.ActiveAttempt.Value,
            worker.Id,
            behavior,
            revision,
            caseId,
            reference,
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, forgedOwner.StatusCode);
        Assert.DoesNotContain(
            SecretTrigger,
            await forgedOwner.Content.ReadAsStringAsync(cancellationToken),
            StringComparison.Ordinal);

        using var forgedTask = await PostLoadAsync(
            client,
            task.Id.Owner.Value,
            NeuronId.For<ITask>(task.Id.Owner, "forged-task"),
            started.ActiveAttempt.Value,
            worker.Id,
            behavior,
            revision,
            caseId,
            reference,
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, forgedTask.StatusCode);

        using var forgedAttempt = await PostLoadAsync(
            client,
            task.Id.Owner.Value,
            task.Id,
            new AttemptId(Guid.Parse("ffffffffffffffffffffffffffffffff")),
            worker.Id,
            behavior,
            revision,
            caseId,
            reference,
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, forgedAttempt.StatusCode);

        var foreignWorker = brain.Neuron<IWorker>("trigger-auth-foreign-worker");
        using var forgedWorker = await PostLoadAsync(
            client,
            task.Id.Owner.Value,
            task.Id,
            started.ActiveAttempt.Value,
            foreignWorker.Id,
            behavior,
            revision,
            caseId,
            reference,
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, forgedWorker.StatusCode);
        Assert.DoesNotContain(
            SecretTrigger,
            await forgedWorker.Content.ReadAsStringAsync(cancellationToken),
            StringComparison.Ordinal);

        using var forgedActivation = await PostLoadAsync(
            client,
            task.Id.Owner.Value,
            task.Id,
            started.ActiveAttempt.Value,
            worker.Id,
            new BehaviorId("com.digitalbrain.forged"),
            revision,
            caseId,
            reference,
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, forgedActivation.StatusCode);
    }

    [Fact(
        Timeout = 90_000,
        DisplayName = "trigger store remains silo grain custody; reverse-broker HTTP store endpoint is absent")]
    public async Task TriggerStoreHttpEndpointIsAbsent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        await using var host = await StartHostAsync(brain, cancellationToken);
        using var client = host.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/behaviors/broker/triggers/store")
        {
            Content = JsonContent.Create(new { owner = "x" }),
        };
        request.Headers.TryAddWithoutValidation(BehaviorBrokerContract.CredentialHeaderName, ValidCredential);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostLoadAsync(
        HttpClient client,
        string owner,
        NeuronId task,
        AttemptId attempt,
        NeuronId worker,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/behaviors/broker/triggers/load")
        {
            Content = JsonContent.Create(new
            {
                owner,
                taskType = task.Type,
                taskOwner = task.Owner.Value,
                taskName = task.Name,
                attempt = attempt.Value.ToString("N"),
                workerType = worker.Type,
                workerOwner = worker.Owner.Value,
                workerName = worker.Name,
                behavior = behavior.Value,
                revision = revision.Value,
                caseId,
                reference = new { id = reference.Id.ToString("N"), expiresAt = reference.ExpiresAt },
            }),
        };
        request.Headers.TryAddWithoutValidation(BehaviorBrokerContract.CredentialHeaderName, ValidCredential);
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task<RunningHost> StartHostAsync(TestBrain brain, CancellationToken cancellationToken)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BehaviorBrokerContract.CredentialConfigurationKey] = ValidCredential,
            })
            .Build();

        var port = GetFreeTcpPort();
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(BehaviorProtectedTriggerLoadAuthorityTests).Assembly.FullName,
        });
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
        builder.Services.AddRouting();
        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddBehaviorBrokerAuthentication(configuration);
        builder.Services.AddSingleton<IGrainFactory>(brain.Cluster.Client);
        builder.Services.AddSingleton<IBehaviorProtectedTriggerAccess>(
            new GrainBehaviorProtectedTriggerAccess(brain.Cluster.Client));
        var app = builder.Build();
        app.UseRouting();
        app.UseBehaviorBrokerAuthentication();
        app.MapGet("/health", () => Results.Text("Healthy"));
        app.MapBehaviorProtectedTriggerBroker();
        await app.StartAsync(cancellationToken);
        return new RunningHost(app, new Uri($"http://127.0.0.1:{port}"));

        static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }

    private sealed class RunningHost(WebApplication app, Uri baseAddress) : IAsyncDisposable
    {
        public HttpClient CreateClient()
            => new() { BaseAddress = baseAddress };

        public async ValueTask DisposeAsync()
            => await app.DisposeAsync();
    }

    private sealed record LoadTriggerResponse(string ContentBase64);
}
