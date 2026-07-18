using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Location;
using Ino.ServiceDefaults;
using Microsoft.Extensions.Hosting;

const int DefaultSiloPort = 11116;
const int DefaultGatewayPort = 30005;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddDomain(new Location(), DefaultSiloPort, DefaultGatewayPort);
builder.AddInoChatClients();

await builder.Build().RunAsync();
