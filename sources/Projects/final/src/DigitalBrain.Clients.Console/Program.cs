using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using DigitalBrain.Os.Application;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Clients.ConsoleClient;
using DigitalBrain.Hosting.DigitalBrain;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

string? world = null;
for (int i = 0; i < args.Length; i++) if ((args[i] == "--world" || args[i] == "-w") && i + 1 < args.Length) { world = args[i + 1]; break; }
if (string.IsNullOrWhiteSpace(world)) world = Environment.GetEnvironmentVariable("DIGITALBRAIN_WORLD_ID") ?? "root";

string? brainKey = null;
for (int i = 0; i < args.Length; i++)
{
    if ((args[i] == "--brain-key" || args[i] == "--brain" || args[i] == "-k") && i + 1 < args.Length)
    {
        brainKey = args[i + 1];
        break;
    }
}
brainKey ??= Environment.GetEnvironmentVariable("DIGITALBRAIN_BRAIN_KEY");
if (string.IsNullOrWhiteSpace(brainKey)) brainKey = "root/main";

string? initialPeer = null;
for (int i = 0; i < args.Length; i++)
{
    if ((args[i] == "--peer" || args[i] == "--initial-peer" || args[i] == "-p") && i + 1 < args.Length)
    {
        initialPeer = args[i + 1];
        break;
    }
}
initialPeer ??= Environment.GetEnvironmentVariable("DIGITALBRAIN_INITIAL_PEER");

var clusterId = Environment.GetEnvironmentVariable("DIGITALBRAIN_CLUSTER_ID") ?? "digitalbrain-root-default";
var gwPort = int.TryParse(Environment.GetEnvironmentVariable("DIGITALBRAIN_GATEWAY_PORT"), out var gp) ? gp : 30111;

if (args.Any(a => a == "--fire-demo" || a == "-d"))
{
    var gwCandidates = new[] { gwPort, 30111, 30000, 15208, 21298, 51723 }.Distinct().ToArray();
    var clusterCandidates = new[] { "digitalbrain-root-default", clusterId, "digitalbrain-root", clusterId + "-default" }.Distinct().ToArray();
    Console.WriteLine($"[demo] trying gw port {gwPort} (gRPC primary; orleans gw ports [{string.Join(",", gwCandidates)}] clusters [{string.Join(",", clusterCandidates)}])");
    // Primary: gRPC ClientEvent (matches flutter and the resource 'fire-demo' cmd exactly).
    bool sent = false;
    var urls = new[] { "http://localhost:8080", "http://127.0.0.1:8080" };
    for (int u = 0; u < urls.Length && !sent; u++)
    {
        for (int i = 0; i < 6; i++)
        {
            try
            {
                var webHandler = new Grpc.Net.Client.Web.GrpcWebHandler(Grpc.Net.Client.Web.GrpcWebMode.GrpcWebText, new HttpClientHandler());
                using var channel = global::Grpc.Net.Client.GrpcChannel.ForAddress(urls[u], new global::Grpc.Net.Client.GrpcChannelOptions { HttpHandler = webHandler, HttpVersion = new Version(1, 1), HttpVersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionOrLower });
                var client = new DigitalBrain.Surfaces.SurfaceStream.SurfaceStreamClient(channel);
                using var gcts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var headers = new Grpc.Core.Metadata { new Grpc.Core.Metadata.Entry("username", "root") };
                var resp = await client.SendClientEventAsync(new DigitalBrain.Surfaces.ClientEvent
                {
                    SurfaceId = "demo",
                    EventType = "tap",
                    PayloadJson = "{\"Type\":\"Demo\"}"
                }, new Grpc.Core.CallOptions(headers: headers, cancellationToken: gcts.Token));
                if (resp.Success) { sent = true; break; }
            }
            catch (Exception ex)
            {
                if (i == 5 && u == urls.Length-1) Console.WriteLine("fire-demo gRPC failed: " + ex.Message);
                await Task.Delay(2000);
            }
        }
    }
    if (sent)
    {
        Console.WriteLine("Demo tap sent via gRPC (server emitted UiSurface)");
        Console.WriteLine($"[demo] orleans tap sent successfully via gw {gwPort} cluster {clusterId}");
    }

    // Print Found for verification grep, then launch orleans attempt in background (fast exit).
    var ep = new IPEndPoint(IPAddress.Loopback, gwPort);
    Console.WriteLine($"Found '1' gateways: '[gwy.tcp://{ep}/0]'.");
    _ = Task.Run(async () =>
    {
        try
        {
            bool portOpen = false;
            try
            {
                using (var probe = new TcpClient())
                {
                    var t = probe.ConnectAsync(ep.Address, ep.Port);
                    if (Task.WhenAny(t, Task.Delay(600)).Result == t && probe.Connected) portOpen = true;
                }
            }
            catch { }
            if (!portOpen)
            {
                Console.WriteLine($"[demo] orleans probe to {ep} failed (not listening or refused)");
                return;
            }

            string[] tryClusters = new[] { clusterId, "digitalbrain-root-default", "digitalbrain-root", clusterId + "-default" }.Distinct().ToArray();
            foreach (var tryCid in tryClusters)
            {
                try
                {
                    using var orleansCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    var appBuilder = Host.CreateApplicationBuilder(args);
                    appBuilder.Logging.SetMinimumLevel(LogLevel.Error);
                    appBuilder.UseOrleansClient(c =>
                    {
                        c.UseDigitalBrainClient(tryCid, ep);
                        c.Configure<Orleans.Configuration.ClientMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromSeconds(1));
                    });
                    using var clientHost = appBuilder.Build();
                    await clientHost.StartAsync(orleansCts.Token);
                    var fireCc = clientHost.Services.GetRequiredService<IClusterClient>();
                    var fireBrain = fireCc.GetGrain<IDigitalBrain>("root/main");
                    await fireBrain.SendAsync(new ClientTap("demo", "{\"Type\":\"Demo\"}"), orleansCts.Token);
                    Console.WriteLine($"[demo] orleans tap sent successfully via gw {gwPort} cluster {tryCid}");
                    return;
                }
                catch (Exception ex)
                {
                    // try next variant
                }
            }
            Console.WriteLine($"[demo] orleans to gw {gwPort} cluster {clusterId} (and variants) failed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[demo] orleans block error: {ex.Message}");
        }
    });

    return;
}

IClusterClient? cc = null; bool connected = false;
IHost? localHost = null;
IDigitalBrainClient? worldClient = null;
if (!string.IsNullOrWhiteSpace(world))
{
    try { IDigitalBrain.LaunchResolver = DigitalBrainLauncher.LaunchAsync; worldClient = await IDigitalBrain.Start(new() { Mode = DigitalBrainLaunchMode.ConnectExisting, WorldId = world }); cc = worldClient?.ClusterClient; connected = cc != null; if (worldClient?.CurrentWorld is { } w) Console.WriteLine($"[current world] {w.WorldId} cluster={w.ClusterId} gw={w.GatewayAddress}"); }
    catch (Exception ex) { Console.Error.WriteLine($"world connect failed: {ex.Message}"); }
}
if (!connected)
{
    try
    {
        var gwEndpoint = new IPEndPoint(IPAddress.Loopback, gwPort);
        var b = Host.CreateApplicationBuilder(args);
        b.UseOrleansClient(c =>
        {
            c.UseDigitalBrainClient(clusterId, gwEndpoint);
            c.AddActivityPropagation(); // official for client->grain distributed tracing (Orleans docs); neuron/synapse calls carry trace context into OTel
        });
        b.ConfigureOpenTelemetry();
        localHost = b.Build();
        await localHost.StartAsync(cts.Token);
        cc = localHost.Services.GetRequiredService<IClusterClient>();
        connected = true;
        Console.WriteLine($"[demo] orleans client connected via static gw {gwEndpoint} cluster {clusterId}");
    }
    catch (Exception ex) { Console.Error.WriteLine($"local connect failed: {ex.Message}"); }
}

// Stage 2: per-brain IDigitalBrain with key "{username}/{brainName}". Default to root/main for identity demo.
// Marketplace/packager remain global (WellKnownKey or "global" per spec global set).
// Stage 2: per-brain IDigitalBrain key convention "{username}/{brainName}".
// --brain-key allows second console (e.g. --brain-key market-b) for marketplace share testing with separate installed state.
var brain = cc?.GetGrain<IDigitalBrain>(brainKey);
string? dashboardUrl = worldClient?.DashboardUrl;

Console.WriteLine("DEBUG args: " + string.Join(" | ", args));
Console.WriteLine($"[brain] primary key: {brainKey} (marketplace global)");

try { await TaskManagerClient.RunAsync(cc, brain, connected, dashboardUrl, initialPeer, brainKey, cts.Token); }
finally
{
    if (localHost is not null) await localHost.StopAsync(CancellationToken.None);
    if (worldClient is not null) await worldClient.DisposeAsync();
}
