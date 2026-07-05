---
name: verify
description: Drive the real running DigitalBrain app (Aspire-hosted Orleans kernel + Flutter client) to confirm a backend change actually works end-to-end.
---

# Verifying DigitalBrain end-to-end

## Launch

```bash
aspire run --project DigitalBrain.AppHost/DigitalBrain.AppHost.csproj
```

Wait for the dashboard URL to print, then use the Aspire MCP tools (`mcp__aspire__select_apphost`,
`list_resources`, `list_console_logs`, `list_structured_logs`, `execute_resource_command` with
`rebuild` to pick up source changes) — never re-implement resource discovery by hand.

Resources to expect healthy: 3 `kernel-*` replicas (Orleans silos), `flutter-ui` (runs as a
**native Windows desktop app** via `flutter run -d windows`, not a browser tab — there is no
web/browser surface to screenshot for this project locally), `storage-*`/`clustering`/`grainstate`/
`journal` (Azurite), `ollama`/`qwen`.

## Driving the app

The Flutter client is a native Windows window — there is no browser or GUI-automation tool
available in this environment to click it programmatically, and `connect_dart_tooling_daemon`
does not work against an Aspire-launched Flutter process (it exposes a raw VM Service URI, not a
DTD URI; that tool errors out asking for `--print-dtd`, which Aspire's launch profile doesn't set).

Instead, drive the kernel's real gRPC endpoint directly — this is the correct surface for
verifying backend routing/identity changes (`HomeFeedBus`, `GatewayService`, per-`clientId`
addressing, etc.), and is what the real Flutter client does under the hood anyway. Get the
kernel's `grpc` URL from `mcp__aspire__list_resources` (e.g. `http://localhost:49588`).

Write a throwaway console project referencing `DigitalBrain.Kernel.csproj` (that project's
`digitalbrain.proto` is compiled with `GrpcServices="Both"`, so the client stub
`DigitalBrain.Runtime.Grpc.DigitalBrainGateway.DigitalBrainGatewayClient` already exists — no
protoc invocation needed):

```csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="E:\brain\DigitalBrain.Kernel\DigitalBrain.Kernel.csproj" />
  </ItemGroup>
</Project>
```

```csharp
using DigitalBrain.Runtime.Grpc;
using Grpc.Core; using Grpc.Net.Client; using Google.Protobuf; using System.Text.Json;

var channel = GrpcChannel.ForAddress("http://localhost:49588");
var client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
using var call = client.WatchHomeFeed(new WatchHomeFeedRequest { ClientId = "my-clientid" }, cancellationToken: cts.Token);
// await foreach (var envelope in call.ResponseStream.ReadAllAsync(cts.Token)) { ... }
//   — catch BOTH OperationCanceledException AND RpcException(StatusCode.Cancelled) when you
//   cancel the stream yourself at the end; ReadAllAsync surfaces the latter, not the former.

await client.SendAsync(new SynapseEnvelope {
    TypeName = "LoginRequest",
    Payload = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(new { username = "...", password = "...", clientId = "my-clientid" }))
});
```

Dev-mode seeded credentials (`admin`/`admin`) always authenticate regardless of how many real
users already exist (bypasses the "only the first user can self-provision" guard) — use them to
get a **second** real logged-in identity for isolation tests; a second made-up username will be
rejected once any real user already exists.

## What to check

- Open two `WatchHomeFeed` connections with distinct `clientId`s, log in as two different real
  users, and grep each connection's received cards for the other user's identifiers — proves
  per-connection addressing end-to-end (`HomeFeedBus` → Orleans stream routing).
- Trigger a chat-driven Salesforce/Gmail-credential-requiring prompt via `InoRequest` and confirm
  the resulting card carries the requesting connection's own `clientId` and never reaches the
  other connection.
- Send a direct `SalesforceAuthRequested`/similar identity-gated signal with no prior login and
  confirm it's rejected (`RpcException` with the expected `StatusCode`), not silently defaulted.
- `LogoutRequest` should cause that same connection's stream to receive a fresh login-kind card.
- Check `mcp__aspire__list_console_logs`/`list_structured_logs` per kernel replica, searching for
  `Exception`, after the drive — a pre-existing, unrelated `SystemStatusNeuron` "SystemStatus MCP
  connect failed" `IntPtr`-serialization warning is expected background noise (self-awareness
  telemetry, degrades gracefully) and is not a sign of a regression.

## Cleanup

Stop the AppHost when done (`Ctrl+C` on the `aspire run` process, or just let the background bash
task get killed) — it doesn't need to stay running between sessions.
