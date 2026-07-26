## 9. ASP.NET Core gRPC over a Kestrel Windows named pipe

ASP.NET Core has first-party named-pipe support in the `Microsoft.AspNetCore.App` shared framework
on .NET 8+. Kestrel listens with `ListenNamedPipe`; gRPC requires HTTP/2. The client uses
`GrpcChannel.ForAddress` with a `SocketsHttpHandler.ConnectCallback` which opens
`NamedPipeClientStream`:

- [gRPC over named pipes, server and client configuration](https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess-namedpipes?view=aspnetcore-10.0)
- [IPC security, server-owner validation, and impersonation guidance](https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess?view=aspnetcore-10.0)
- [ASP.NET Core gRPC service setup](https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore?view=aspnetcore-10.0)
- [gRPC .NET client](https://learn.microsoft.com/en-us/aspnet/core/grpc/client?view=aspnetcore-10.0)

The official Kestrel transport already exposes:

- `ListenNamedPipe(pipeName, listen => listen.Protocols = HttpProtocols.Http2)`;
- `NamedPipeTransportOptions.CurrentUserOnly`;
- `NamedPipeTransportOptions.PipeSecurity`;
- `NamedPipeTransportOptions.CreateNamedPipeServerStream` for per-endpoint customization.

Do not add a `Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes` NuGet reference: the assembly
ships in `Microsoft.AspNetCore.App`. Do not add a community named-pipe gRPC transport.

### Initial protocol: unary, one reused channel

The first Behavior runtime permits sequential capability calls and one worker is scoped to one
execution. Use:

```proto
service BehaviorBroker {
  rpc ClaimExecution(ClaimExecutionRequest) returns (ExecutionStart);
  rpc InvokeCapability(CapabilityCall) returns (CapabilityResult);
  rpc CompleteExecution(ExecutionCompletion) returns (ExecutionAck);
}
```

The worker receives only the pipe name plus an opaque one-time execution bootstrap credential,
connects, claims its committed input, reuses the channel for capability calls, and completes.

Microsoft recommends reusing channels. Bidirectional streaming can reduce repeated HTTP/2 request
overhead, but Microsoft's guidance calls it an advanced optimization with restart, ordering, and
single-writer complexity. It is not justified until measurement shows unary calls are a bottleneck:

- [gRPC performance guidance and channel reuse](https://learn.microsoft.com/en-us/aspnet/core/grpc/performance?view=aspnetcore-10.0)
- [Streaming reader/writer concurrency rules](https://learn.microsoft.com/en-us/aspnet/core/grpc/services?view=aspnetcore-10.0)

### Exact packages

Pin the current official stable gRPC family together:

| Project role | Package | Version |
| --- | --- | --- |
| Shared `.proto` contract | `Grpc.Tools` (`PrivateAssets="all"`) | `2.80.0` |
| Shared `.proto` contract | `Google.Protobuf` | `3.31.1` |
| Shared generated service types if needed | `Grpc.Core.Api` | `2.80.0` |
| Trusted ASP.NET Core broker host | `Grpc.AspNetCore` | `2.80.0` |
| Runtime worker client | `Grpc.Net.Client` | `2.80.0` |

`Grpc.AspNetCore` 2.80.0 itself pins `Grpc.Tools` 2.80.0 and `Google.Protobuf` 3.31.1 for `net10.0`,
so these versions avoid an unnecessary independent upgrade:

- [`Grpc.AspNetCore` 2.80.0](https://www.nuget.org/packages/Grpc.AspNetCore/2.80.0)
- [`Grpc.Net.Client` 2.80.0](https://www.nuget.org/packages/Grpc.Net.Client/2.80.0)
- [`Grpc.Tools` 2.80.0](https://www.nuget.org/packages/Grpc.Tools/2.80.0)
- [`Google.Protobuf` 3.31.1](https://www.nuget.org/packages/Google.Protobuf/3.31.1)

### Transport configuration obligations

- Set HTTP/2 explicitly.
- Bind no TCP endpoint in the worker-broker host.
- Restrict the pipe with `CurrentUserOnly` and an explicit least-privilege `PipeSecurity`; do not
  copy the documentation's broad `Users` example.
- The client uses `TokenImpersonationLevel.Anonymous` or `None`.
- The client validates the pipe server owner before sending the bootstrap credential.
- Bound gRPC send/receive message sizes, execution/call deadlines, and pending calls.
- Dispose the channel/process on execution termination.
- Do not depend on gRPC client-side load balancing or channel connectivity-state APIs: Microsoft
  documents that channels using `ConnectCallback` do not support them.

### Required proof

- The correct restricted worker can connect; a process under an unapproved identity cannot.
- The client rejects a pipe server owned by an unexpected SID.
- Endpoint enumeration proves the host opened no TCP listener.
- Oversized messages, unknown `oneof`/message versions, expired credentials, wrong execution IDs,
  duplicate ordinals, and late completion are rejected.
- Killing the pipe/worker cancels in-flight RPC and releases the execution lease without losing the
  durable execution record.

## 10. Exact task ordering for the implementation plan

1. **Orleans dependency gate**
   - Add resolved-package and durability characterization tests.
   - Keep the coherent RC family; directly pin standalone serialization.
2. **Kernel result-aware delegation**
   - Add execution/ordinal/fingerprint metadata.
   - Add generated method result codecs.
   - Change terminal authority/filter so result recording commits before return.
   - Prove worker-loss replay and terminal-write failure.
3. **Generic Behavior identity**
   - Add the sole `[GrainType("behavior")]` implementation.
   - Replace/remove the Flutter concrete Behavior in the same change.
   - Add the minimal protected generic dispatch seam while retaining base delivery, journal,
     dedupe, filters, and outbox.
4. **Durable execution queue**
   - Add execution receipt/state and outbox handoff.
   - Add owner-scoped queue/lease state plus reminder reconciliation.
   - Add the host `BackgroundService`; no grain `Task.Run`.
5. **Generated capability catalogs/adapters**
   - Extend `DigitalBrain.SourceGeneration`.
   - Add standalone serializer bootstrapping from the exact approved contract catalog.
6. **Fixed Protobuf IPC contract**
   - Add the shared protocol project and the exact gRPC pins.
   - Keep CLR payloads opaque and exact-type decoded by generated adapters.
7. **Named-pipe broker host and worker client**
   - Add Kestrel HTTP/2 named-pipe endpoint, unary service, channel reuse, limits, identity
     validation, and cancellation.
8. **Runtime worker**
   - Claim committed execution, load the approved revision, execute sequential calls, and submit a
     correlated completion.
9. **Recovery and chaos proofs**
   - Worker death, broker/host restart, silo restart, reminder tick loss, terminal storage failure,
     duplicated RPC, and replay mismatch.
10. **Product migration and deletion**
    - Move boot/UI and enrichment behavior to this rail.
    - Delete the old concrete Behavior, pull compositions, redundant docs, projects, and references
      only after root BDD parity is green.

## 11. Libraries explicitly rejected

| Library/approach | Reason |
| --- | --- |
| Akka.NET, Proto.Actor, Dapr Actors | Duplicates Orleans identity, placement, calls, and lifecycle |
| Marten/EventStoreDB/custom event-sourcing package | Duplicates the existing `DurableGrain` journal and commit model |
| Hangfire, Quartz.NET | Duplicates durable queue/lease/reminder reconciliation |
| MassTransit, NServiceBus, cloud queue for local handoff | Adds a second message fabric before the current durable outbox is shown insufficient |
| `DispatchProxy`, Castle DynamicProxy | Runtime reflection and late failures where source generation can prove the contract |
| protobuf-net.Grpc, MagicOnion | Duplicates official `.proto`/`Grpc.Tools` generated contracts and weakens language-neutral wire ownership |
| Community named-pipe gRPC transports | Kestrel and `SocketsHttpHandler.ConnectCallback` ship the required transport |
| Orleans client inside the worker | Gives untrusted code cluster authority and generated grain proxies |
| `Task.Run`/in-memory `TaskCompletionSource` from a grain | Escapes durable activation state and cannot be recovered |
| Orleans Streams for the first execution queue | Adds provider/checkpoint semantics while a single durable queue neuron and current outbox suffice |
