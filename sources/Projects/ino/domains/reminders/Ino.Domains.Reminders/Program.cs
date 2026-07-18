using Aspire.IAW;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Reminders;
using Ino.ServiceDefaults;
using Microsoft.Extensions.Hosting;

const int DefaultSiloPort = 11117;
const int DefaultGatewayPort = 30006;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// IAW substrate must come before AddDomain — RemindersNeuron : LlmNeuron
// inherits IAW.Core.Agent and depends on the [AgentState] mapper +
// ILocalDurableJobManager + IChatClient pipeline that AddIAW registers.
// Other domain silos (Travel/Taxi/Location) don't need this yet because
// their neurons use plain Neuron<TEvent>; Reminders is the first IAW→ino
// capability bridge to actually exercise the substrate (Phase 4 Slice B).
builder.AddIAW();

builder.AddDomain(new Reminders(), DefaultSiloPort, DefaultGatewayPort);
builder.AddInoChatClients();

await builder.Build().RunAsync();
