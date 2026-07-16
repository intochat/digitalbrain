using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using DigitalBrain.V2.Ui.Grpc;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
using var channel = GrpcChannel.ForAddress("https://localhost:58997", new GrpcChannelOptions { HttpHandler = handler });
var client = new DigitalBrainV2Ui.DigitalBrainV2UiClient(channel);
const string audience = "digitalbrain-v3-ui";
var bootstrapHeaders = new Metadata { { "x-v2-audience", audience } };
var session = await client.BootstrapSessionAsync(new BootstrapSessionRequest { Username = "admin", Password = "admin" }, bootstrapHeaders);
Console.WriteLine($"session actor={session.ActorId} owner={session.OwnerId}");
var headers = new Metadata
{
    { "x-v2-session", session.AccessToken },
    { "x-v2-audience", audience },
};
using var call = client.WatchSurfaceFeed(new WatchSurfaceFeedRequest
{
    AfterSequence = 0,
    Audience = FeedAudienceKind.Actor,
    MaxBatchSize = 50,
    ClientCapabilities = { "ui.protocol.v2", "ui.payload.native", "ui.native.ino-conversation", "ui.native.typed-actions", "ui.native.feature-approval", "ui.native.feed-reset", "ui.native.feed-ack", "ui.widget-vocabulary.v2", "ui.payload.widgetTree", "ui.payload.rfw" }
}, headers);
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
while (await call.ResponseStream.MoveNext(cts.Token))
{
    var ev = call.ResponseStream.Current;
    if (ev.EventCase == SurfaceFeedEvent.EventOneofCase.Reset)
    {
        Console.WriteLine($"RESET reason={ev.Reset.Reason} resume={ev.Reset.ResumeSequence} snaps={ev.Reset.SnapshotJson.Count}");
        foreach (var snap in ev.Reset.SnapshotJson) Dump(snap);
        break;
    }
    if (ev.EventCase == SurfaceFeedEvent.EventOneofCase.SurfaceJson)
    {
        Dump(ev.SurfaceJson);
        break;
    }
}

static void Dump(string json)
{
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;
    Console.WriteLine(json);
}
