using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using DigitalBrain.Core;
using DigitalBrain.Runtime.Grpc;
using Google.Protobuf;
using Grpc.Net.Client;

namespace DigitalBrain.Tests.E2E;

public class DigitalBrainAppHostFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = null!;

    // The browser navigates here: the kernel "web" endpoint (Http1AndHttp2) serves the Flutter
    // bundle and gRPC-Web. Native gRPC helpers must NOT use this — over cleartext an Http1AndHttp2
    // endpoint answers HTTP/1.1 (no ALPN), so an HTTP/2 gRPC call gets HTTP_1_1_REQUIRED.
    public string GatewayHttpsUrl { get; private set; } = null!;

    // The native-gRPC helpers dial here: the kernel "grpc" endpoint (Http2-only).
    public string GrpcUrl { get; private set; } = null!;

    // True once prerequisites are met and GatewayHttpsUrl/GrpcUrl are valid navigation/dial
    // targets -- either from a freshly booted AppHost or an attached warm cluster. Distinct from
    // `App is null`, which is also true after a successful warm-cluster attach.
    public bool Ready { get; private set; }

    // The bare-Kernel non-Aspire-hosted fast path (Program.cs's isAspireHosted=false branch): fixed
    // Kestrel ports, in-memory Orleans clustering. A developer runs
    // `dotnet run --project DigitalBrain.Kernel` (DIGITALBRAIN_WEBROOT set) and leaves it running;
    // InitializeAsync attaches to it instead of booting a fresh ~30-120s Aspire stack.
    internal const string WarmClusterWebUrl = "http://localhost:8081";
    internal const string WarmClusterGrpcUrl = "http://localhost:8080";

    internal static async Task<bool> ProbeAsync(string url, TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = timeout };
        try
        {
            using var response = await client.GetAsync(url);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public virtual async Task InitializeAsync()
    {
        if (!E2EPrerequisites.OptedIn)
        {
            return; // Not opted into the real-stack E2E; the [SkippableFact] will skip.
        }

        if (await ProbeAsync(WarmClusterWebUrl, TimeSpan.FromSeconds(2)))
        {
            // Port 8080 is HTTP/2-only cleartext (h2c) -- the .NET gRPC client needs this switch to call
            // it without TLS. This is a process-wide AppContext switch (there is no per-handler
            // equivalent); it only permits cleartext HTTP/2 for channels that explicitly target an
            // http:// address, so it does not affect this process's other (HTTPS) gRPC channels.
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            GatewayHttpsUrl = WarmClusterWebUrl;
            GrpcUrl = WarmClusterGrpcUrl;
            Ready = true;
            return; // App stays null: attached to a warm cluster we don't own, nothing to boot or dispose.
        }

        var testId = Guid.NewGuid().ToString("N")[..8];
        Environment.SetEnvironmentVariable("DIGITALBRAIN_TEST_MODE", "true");
        Environment.SetEnvironmentVariable("DIGITALBRAIN_SURFACES_ENABLED", "true");
        Environment.SetEnvironmentVariable("DigitalBrain__ClusterId", $"e2e-{testId}");
        Environment.SetEnvironmentVariable("DIGITALBRAIN_KERNEL_REPLICAS",
            Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_REPLICAS") ?? "1");

        // Resolve the AppHost entry point type from the referenced assembly without pulling duplicate Program symbols into global scope.
        var appHostAssembly = Assembly.Load("DigitalBrain.AppHost");
        var programType = appHostAssembly.GetTypes().FirstOrDefault(t => t.Name == "Program")
                          ?? appHostAssembly.EntryPoint?.DeclaringType
                          ?? throw new InvalidOperationException("Could not locate AppHost Program type for DistributedApplicationTestingBuilder.");

        var builder = await DistributedApplicationTestingBuilder.CreateAsync(programType);

        foreach (var parameter in builder.Resources.OfType<ParameterResource>())
        {
            builder.Configuration[$"Parameters:{parameter.Name}"] = "e2e-test";
        }

        App = await builder.BuildAsync();
        await App.StartAsync();

        using var startupCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await App.ResourceNotifications.WaitForResourceHealthyAsync("kernel", startupCts.Token);

        // Browser nav target: the kernel "web" endpoint (static bundle + gRPC-Web).
        string webUrl = "https://localhost:8080";
        try { webUrl = App.GetEndpoint("kernel", "web").ToString(); } catch { }
        GatewayHttpsUrl = webUrl;

        // Native-gRPC target: the kernel "grpc" endpoint (Http2). Falls back to the web URL only
        // if "grpc" is unavailable.
        string grpcUrl = webUrl;
        try { grpcUrl = App.GetEndpoint("kernel", "grpc").ToString(); } catch { }
        GrpcUrl = grpcUrl;
        Ready = true;
    }

    public virtual async Task DisposeAsync()
    {
        if (App is not null)
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    public GrpcChannel CreateGatewayGrpcChannel()
    {
        return GrpcChannel.ForAddress(GrpcUrl);
    }

    public async Task SendSynapseAsync(string typeName, string jsonPayload, string? correlationId = null)
    {
        using var channel = CreateGatewayGrpcChannel();
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        await client.SendAsync(new SynapseEnvelope
        {
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
            TypeName = typeName,
            Payload = ByteString.CopyFromUtf8(jsonPayload)
        });
    }

    public async Task SendExperienceStepAsync(string pack, string experienceId, string eventName, IReadOnlyDictionary<string, string>? args = null)
    {
        using var channel = CreateGatewayGrpcChannel();
        var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

        var payload = new Dictionary<string, string>(args ?? new Dictionary<string, string>())
        {
            ["pack"] = pack,
            ["experienceId"] = experienceId,
            ["eventName"] = eventName,
        };

        await client.SendAsync(new SynapseEnvelope
        {
            CorrelationId = "e2e-step-" + eventName,
            TypeName = nameof(ExperienceStep),
            Payload = ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(payload))
        });
    }
}
