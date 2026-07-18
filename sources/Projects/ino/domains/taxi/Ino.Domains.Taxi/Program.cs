using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Taxi;
using Ino.ServiceDefaults;
using Microsoft.Extensions.Hosting;

const int DefaultSiloPort = 11115;
const int DefaultGatewayPort = 30004;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddDomain(new Taxi(), DefaultSiloPort, DefaultGatewayPort);
builder.AddInoChatClients();

await builder.Build().RunAsync();
