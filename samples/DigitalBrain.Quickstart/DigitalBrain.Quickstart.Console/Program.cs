using DigitalBrain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (args.Contains("--command-contract", StringComparer.Ordinal))
{
    var generated = new Queue<ConversationId>(
        [new ConversationId("generated-1")]);
    var commands = new QuickstartCommands(() => generated.Dequeue());
    foreach (var input in new[]
             {
                 "/role reasoning",
                 "/new",
                 "/conversation conversation-1",
                 "/help",
                 "/exit"
             })
    {
        var result = commands.Apply(input);
        Console.WriteLine(result.Message);
    }
    return;
}

var builder = Host.CreateApplicationBuilder(args);
if (!builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "The DigitalBrain quickstart console is disabled outside Development.");

var ownerValue = builder.Configuration["DigitalBrain:DevTools:Owner"];
if (string.IsNullOrWhiteSpace(ownerValue))
    throw new InvalidOperationException(
        "Set the explicit digitalbrain-owner Development parameter.");

builder.AddDigitalBrainClient("brain");
using var host = builder.Build();

if (args.Contains("--environment-probe", StringComparer.Ordinal))
{
    await host.RunAsync();
    return;
}

if (args.Contains("--startup-contract", StringComparer.Ordinal))
{
    using var unauthorizedScope = host.Services.CreateScope();
    try
    {
        _ = unauthorizedScope.ServiceProvider.GetRequiredService(
            typeof(DigitalBrainClient));
        throw new InvalidOperationException(
            "DigitalBrainClient resolved without an owner session.");
    }
    catch (BrainException)
    {
        Console.WriteLine("owner-guard:ok");
    }

    var startupSessions =
        host.Services.GetRequiredService<DigitalBrainSessionFactory>();
    await using var startupSession =
        startupSessions.Create(new BrainOwnerId(ownerValue));
    Console.WriteLine($"owner-session:{ownerValue}");
    Console.WriteLine($"client:{startupSession.Client.GetType().Name}");
    return;
}

await host.StartAsync();
try
{
    var sessions = host.Services.GetRequiredService<DigitalBrainSessionFactory>();
    await using var session = sessions.Create(new BrainOwnerId(ownerValue));
    var shell = new QuickstartConsole(
        session.Client,
        Console.In,
        Console.Out,
        () => new ConversationId($"quickstart-{Guid.NewGuid():N}"));
    await shell.RunAsync();
}
finally
{
    await host.StopAsync();
}
