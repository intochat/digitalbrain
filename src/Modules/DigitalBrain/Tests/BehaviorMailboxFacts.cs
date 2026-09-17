using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;
using DigitalBrain.Core.Behavior;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class BehaviorMailboxFacts
{
    [Theory]
    [InlineData("welcome", """{"name":"Ada"}""", "Completed", "message", "Hello, Ada!")]
    [InlineData("qualified-lead", """{"name":"Ada","score":85}""", "Completed", "qualified", "True")]
    [InlineData("qualified-lead-low", """{"name":"Ada","score":10}""", "Filtered", null, null)]
    [InlineData("contact-cards", """{"contacts":[{"name":"Ada","email":"ada@example.com"}]}""", "Completed", null, null)]
    [InlineData("invoice-total", """{"items":[{"amount":12.5},{"amount":7.5}]}""", "Completed", "total", "20.0")]
    [InlineData("order-summary", """{"items":[{"price":10},{"price":15}]}""", "Completed", "count", "2")]
    [InlineData("priority-message", """{"text":"Urgent: review needed","channel":"chat"}""", "Completed", "priority", "high")]
    [InlineData("csharp-discount", """{"price":100}""", "Completed", "discounted", "90.0")]
    [InlineData("timer-status", "{}", "Completed", "ok", "True")]
    [InlineData("agent-memory", """{"name":"Ada","favoriteColor":"violet"}""", "Completed", "ok", "True")]
    public async Task ExampleGraphsComplete(string name, string inputJson, string status, string? property, string? expected)
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton<IBehaviorNodeExecutor, StubNodeExecutor>(),
        });
        var programs = simulation.SiloServices.GetRequiredService<BehaviorService>();
        var definition = Graph(name);
        await programs.DeployAsync(definition, cancellationToken: cancel);

        using var input = JsonDocument.Parse(inputJson);
        var runId = "run" + name.Replace("-", "", StringComparison.Ordinal)[..Math.Min(12, name.Replace("-", "", StringComparison.Ordinal).Length)];
        await programs.RunAsync(definition.Id, input.RootElement.Clone(), runId, cancel);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        BehaviorRunSnapshot? finished = null;
        while (!timeout.IsCancellationRequested)
        {
            finished = await programs.ReadRunAsync(definition.Id, runId, timeout.Token);
            if (finished is { Status: not "Running" })
            {
                break;
            }

            await Task.Delay(40, timeout.Token);
        }

        Assert.NotNull(finished);
        Assert.Equal(status, finished.Status);
        Assert.Contains(runId, (await programs.ReadAsync(definition.Id, cancel)).Runs);
        if (property is not null)
        {
            var value = finished.Output.GetProperty(property);
            Assert.Equal(expected, value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString());
        }
    }

    [Fact]
    public async Task CancelRecordsTheRun()
    {
        var cancel = TestContext.Current.CancellationToken;
        await using var simulation = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton<IBehaviorNodeExecutor, SlowNodeExecutor>(),
        });
        var programs = simulation.SiloServices.GetRequiredService<BehaviorService>();
        await programs.DeployAsync(Graph("agent-memory"), cancellationToken: cancel);
        using var input = JsonDocument.Parse("""{"name":"Ada","favoriteColor":"violet"}""");
        await programs.RunAsync("agentmemory", input.RootElement.Clone(), "runcancel1", cancel);
        await programs.CancelAsync("agentmemory", "runcancel1", cancel);
        Assert.Equal("Cancelled", (await programs.ReadRunAsync("agentmemory", "runcancel1", cancel))?.Status);
    }

    private static BehaviorDefinition Graph(string name) => name switch
    {
        "welcome" => BehaviorBuilder.Define("welcome", "Personal greeting")
            .On("WelcomeRequested")
            .Node("input", "input")
            .Node("greeting", "template", """{"template":{"message":"Hello, {{input.name}}!"}}""")
            .Node("output", "output")
            .Connect("input", "greeting").Connect("greeting", "output").Build(),
        "qualified-lead" or "qualified-lead-low" => BehaviorBuilder.Define("qualifiedlead", "Qualify a lead")
            .On("LeadReceived")
            .Node("input", "input")
            .Node("qualify", "filter", """{"path":"input.score","operator":"gte","value":70}""")
            .Node("output", "output", """{"template":{"name":"{{value.name}}","score":"{{value.score}}","qualified":true}}""")
            .Connect("input", "qualify").Connect("qualify", "output").Build(),
        "contact-cards" => BehaviorBuilder.Define("contactcards", "Map contact cards")
            .On("ContactsReceived")
            .Node("input", "input")
            .Node("cards", "map", """{"path":"input.contacts","template":{"title":"{{value.name}}","email":"{{value.email}}"}}""")
            .Node("output", "output")
            .Connect("input", "cards").Connect("cards", "output").Build(),
        "invoice-total" => BehaviorBuilder.Define("invoicetotal", "Sum invoice amounts")
            .On("InvoiceReceived")
            .Node("input", "input")
            .Node("total", "aggregate", """{"path":"input.items","operation":"sum","field":"amount"}""")
            .Node("output", "output", """{"template":{"total":"{{value}}"}}""")
            .Connect("input", "total").Connect("total", "output").Build(),
        "order-summary" => BehaviorBuilder.Define("ordersummary", "Branch and join an order")
            .On("OrderReceived")
            .Node("input", "input")
            .Node("count", "aggregate", """{"path":"input.items","operation":"count"}""")
            .Node("total", "aggregate", """{"path":"input.items","operation":"sum","field":"price"}""")
            .Node("output", "output", """{"template":{"count":"{{nodes.count}}","total":"{{nodes.total}}"}}""")
            .Connect("input", "count").Connect("input", "total")
            .Connect("count", "output").Connect("total", "output").Build(),
        "priority-message" => BehaviorBuilder.Define("prioritymessage", "Route a priority message")
            .On("MessageReceived")
            .Node("input", "input")
            .Node("priority", "filter", """{"all":[{"path":"input.text","operator":"contains","value":"urgent"},{"path":"input.channel","operator":"in","value":["chat","email"]}]}""")
            .Node("output", "output", """{"template":{"text":"{{value.text}}","priority":"high"}}""")
            .Connect("input", "priority").Connect("priority", "output").Build(),
        "csharp-discount" => BehaviorBuilder.Define("csharpdiscount", "Calculate a discount in C#")
            .On("PriceReceived")
            .Node("input", "input")
            .Node("discount", "code", """{"source":"stub"}""")
            .Node("output", "output")
            .Connect("input", "discount").Connect("discount", "output").Build(),
        "timer-status" => BehaviorBuilder.Define("timerstatus", "Read a timer neuron")
            .On("TimerStatusRequested")
            .Node("input", "input")
            .Node("timer", "agent", """{"mode":"transform"}""")
            .Node("output", "output")
            .Connect("input", "timer").Connect("timer", "output").Build(),
        "agent-memory" => BehaviorBuilder.Define("agentmemory", "Create an agent and follow up")
            .On("AgentMemoryRequested")
            .Node("input", "input")
            .Node("creator", "agent", """{"mode":"spawn"}""")
            .Node("output", "output")
            .Connect("input", "creator").Connect("creator", "output").Build(),
        _ => throw new ArgumentException(name),
    };

    private sealed class StubNodeExecutor : IBehaviorNodeExecutor
    {
        public Task<JsonElement> ExecuteAsync(BehaviorExecutionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.Node.Kind == "code")
            {
                var price = context.Value.GetProperty("price").GetDecimal();
                return Task.FromResult(JsonSerializer.SerializeToElement(new { price, discounted = decimal.Round(price * 0.9m, 2) }));
            }

            return Task.FromResult(JsonSerializer.SerializeToElement(new { ok = true }));
        }
    }

    private sealed class SlowNodeExecutor : IBehaviorNodeExecutor
    {
        public async Task<JsonElement> ExecuteAsync(BehaviorExecutionContext context, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            return JsonSerializer.SerializeToElement(new { ok = true });
        }
    }
}
