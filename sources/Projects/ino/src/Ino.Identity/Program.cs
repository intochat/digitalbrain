using Ino.Core.Hosting.Llm;
using Ino.Identity;
using Ino.ServiceDefaults;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddIdentity();
builder.AddInoChatClients();

await builder.Build().RunAsync();
