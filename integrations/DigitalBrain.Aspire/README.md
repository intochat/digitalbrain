# DigitalBrain.Aspire

`DigitalBrain.Aspire` connects a .NET host to a DigitalBrain resource through the restricted Aspire client projection.

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.AddDigitalBrainClient("brain");
using var host = builder.Build();
await host.StartAsync();

var sessions = host.Services.GetRequiredService<DigitalBrainSessionFactory>();
await using var session = sessions.Create(new BrainOwnerId("organization/space"));
var conversation = session.Client.Conversations.Open(new ConversationId("support"));
```

When an Aspire AppHost references `brain.AsClient()`, the integration consumes the emitted Orleans clustering and Azure Tables connection configuration. A direct host may instead provide `ConnectionStrings:brain`; the integration supplies deterministic local Orleans identifiers and uses the same Azure Tables clustering path.

The registration validates its metadata and connection at startup, registers storage health checks, and enables provider-neutral DigitalBrain and Orleans tracing and metrics.
