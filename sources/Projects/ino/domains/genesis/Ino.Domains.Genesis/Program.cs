using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Genesis.Contracts;
using Ino.ServiceDefaults;
using Microsoft.Extensions.Hosting;

const int DefaultSiloPort = 11119;
const int DefaultGatewayPort = 30008;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// Genesis carries no LlmNeuron-based grains today — CreatorNeuron is
// pure-code (deterministic v0.1 draft body). If future slices upgrade
// CreatorNeuron to LlmNeuron for richer body synthesis, switch this silo
// to builder.AddIAW() (and the AppHost adds .WithReference(ino.Iaw)) the
// way Reminders / Recall do.
builder.AddDomain(new Genesis(), DefaultSiloPort, DefaultGatewayPort);
builder.AddInoChatClients();

await builder.Build().RunAsync();
