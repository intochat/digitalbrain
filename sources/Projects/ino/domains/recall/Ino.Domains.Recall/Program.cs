using Aspire.IAW;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Recall;
using Ino.ServiceDefaults;
using Microsoft.Extensions.Hosting;

const int DefaultSiloPort = 11118;
const int DefaultGatewayPort = 30007;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// IAW substrate before AddDomain — RecallNeuron depends on IMemoryLookup
// (Qdrant-backed) which AddIAW registers. Same wiring as Reminders.
builder.AddIAW();

builder.AddDomain(new Recall(), DefaultSiloPort, DefaultGatewayPort);
builder.AddInoChatClients();

await builder.Build().RunAsync();
