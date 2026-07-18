using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Travel;
using Ino.ServiceDefaults;
using Microsoft.Extensions.Hosting;

const int DefaultSiloPort = 11114;
const int DefaultGatewayPort = 30003;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddDomain(new Travel(), DefaultSiloPort, DefaultGatewayPort);
builder.AddInoChatClients();

await builder.Build().RunAsync();
