using System.Text.Json;
using DigitalBrain.Abstractions.Programming;
using DigitalBrain.Core.Programming;

namespace IntoChat;

public static class ProgramExamples
{
    public static ProgramDefinition IntoChat { get; } = ProgramBuilder.Define("intochat", "IntoChat conversation")
        .On("ChatMessage")
        .Describe("The live IntoChat conversation. Edit the assistant instructions to change future conversations.")
        .Node("input", "input", outputType: "ChatMessage")
        .Node("assistant", "agent", """{"mode":"conversation"}""", inputType: "ChatMessage")
        .Node("output", "output")
        .Connect("input", "assistant").Connect("assistant", "output")
        .Build();

    public static IReadOnlyList<ProgramDefinition> All { get; } = Array.AsReadOnly<ProgramDefinition>(
    [
        ProgramBuilder.Define("welcome", "Personal greeting")
            .On("WelcomeRequested").Describe("Welcome someone by name. Example input: {\"name\":\"Ada\"}.")
            .Node("input", "input")
            .Node("greeting", "template", """{"template":{"message":"Hello, {{input.name}}!"}}""")
            .Node("output", "output")
            .Connect("input", "greeting").Connect("greeting", "output").Build(),

        ProgramBuilder.Define("qualified-lead", "Qualify a lead")
            .On("LeadReceived").Describe("Keep leads scoring at least 70. Example input: {\"name\":\"Ada\",\"score\":85}.")
            .Node("input", "input", outputType: "Lead")
            .Node("qualify", "filter", """{"path":"input.score","operator":"gte","value":70}""", inputType: "Lead", outputType: "Lead")
            .Node("output", "output", """{"template":{"name":"{{value.name}}","score":"{{value.score}}","qualified":true}}""")
            .Connect("input", "qualify").Connect("qualify", "output").Build(),

        ProgramBuilder.Define("contact-cards", "Map contact cards")
            .On("ContactsReceived").Describe("Project a contacts array into cards. Example input: {\"contacts\":[{\"name\":\"Ada\",\"email\":\"ada@example.com\"}]}.")
            .Node("input", "input")
            .Node("cards", "map", """{"path":"input.contacts","template":{"title":"{{value.name}}","email":"{{value.email}}"}}""")
            .Node("output", "output")
            .Connect("input", "cards").Connect("cards", "output").Build(),

        ProgramBuilder.Define("invoice-total", "Sum invoice amounts")
            .On("InvoiceReceived").Describe("Sum decimal amounts without converting them to text. Example input: {\"items\":[{\"amount\":12.5},{\"amount\":7.5}]}.")
            .Node("input", "input")
            .Node("total", "aggregate", """{"path":"input.items","operation":"sum","field":"amount"}""")
            .Node("output", "output", """{"template":{"total":"{{value}}"}}""")
            .Connect("input", "total").Connect("total", "output").Build(),

        ProgramBuilder.Define("order-summary", "Branch and join an order")
            .On("OrderReceived").Describe("Count items and sum prices in two branches, then combine their results. Example input: {\"items\":[{\"price\":10},{\"price\":15}]}.")
            .Node("input", "input")
            .Node("count", "aggregate", """{"path":"input.items","operation":"count"}""")
            .Node("total", "aggregate", """{"path":"input.items","operation":"sum","field":"price"}""")
            .Node("output", "output", """{"template":{"count":"{{nodes.count}}","total":"{{nodes.total}}"}}""")
            .Connect("input", "count").Connect("input", "total")
            .Connect("count", "output").Connect("total", "output").Build(),

        ProgramBuilder.Define("priority-message", "Route a priority message")
            .On("MessageReceived").Describe("Accept urgent messages from a supported channel. Example input: {\"text\":\"Urgent: review needed\",\"channel\":\"chat\"}.")
            .Node("input", "input")
            .Node("priority", "filter", """{"all":[{"path":"input.text","operator":"contains","value":"urgent"},{"path":"input.channel","operator":"in","value":["chat","email"]}]}""")
            .Node("output", "output", """{"template":{"text":"{{value.text}}","priority":"high"}}""")
            .Connect("input", "priority").Connect("priority", "output").Build(),

        ProgramBuilder.Define("timer-status", "Read a timer neuron")
            .On("TimerStatusRequested").Describe("Call the existing timer read capability through the same graph. Input: {}. This reads timer:example; it does not schedule a timer.")
            .Node("input", "input")
            .Node("timer", "call", """{"neuron":"timer:example","interface":"timer","method":"read","arguments":{}}""")
            .Node("output", "output")
            .Connect("input", "timer").Connect("timer", "output").Build(),

        ProgramBuilder.Define("csharp-discount", "Calculate a discount in C#")
            .On("PriceReceived").Describe("Trusted local C#: apply a ten percent discount. Deploying enables this code to run with the server account's local permissions. Example input: {\"price\":100}.")
            .Node("input", "input")
            .Node("discount", "code", JsonSerializer.SerializeToElement(new
            {
                source = """
                    using System.Text.Json;
                    using var input = JsonDocument.Parse(await Console.In.ReadToEndAsync());
                    var price = input.RootElement.GetProperty("price").GetDecimal();
                    Console.WriteLine(JsonSerializer.Serialize(new { price, discounted = decimal.Round(price * 0.9m, 2) }));
                    """,
            }))
            .Node("output", "output")
            .Connect("input", "discount").Connect("discount", "output").Build(),
    ]);
}
