using System.Text.Json;
using DigitalBrain.Abstractions.Behavior;
namespace DigitalBrain.Core.Behavior;

public static class BehaviorExamples
{
    public static BehaviorDefinition IntoChat { get; } = BehaviorBuilder.Define("intochat", "IntoChat conversation")
        .On("ChatMessage")
        .Describe("The live IntoChat conversation. Edit the assistant instructions to change future conversations.")
        .Node("input", "input", outputType: "ChatMessage")
        .Node("assistant", "agent", """{"mode":"conversation"}""", inputType: "ChatMessage")
        .Node("output", "output")
        .Connect("input", "assistant").Connect("assistant", "output")
        .Build();

    public static IReadOnlyList<BehaviorDefinition> All { get; } = Array.AsReadOnly<BehaviorDefinition>(
    [
        BehaviorBuilder.Define("spawn-researchers", "Create a research team")
            .On("ResearchTeamRequested")
            .Describe("Create an independent agent for each company, collect their sourced results, and retire them after the run. Example input: {\"companies\":[{\"name\":\"JetBrains\"},{\"name\":\"37signals\"}]}. Each agent uses its selected LLM and lookup_company tool; model selection is optional.")
            .Node("input", "input")
            .Node("creator", "agent", """{"mode":"spawn","items":"{{input.companies}}","name":"Research {{value.name}}","instructions":"Find published company contact details. Use lookup_company and return its address, email, sources and missing-field notes as JSON. Never guess.","prompt":"Research {{value.name}}","tools":["lookup_company"],"lifetime":"run"}""")
            .Node("results", "agent", """{"mode":"collect","agent":"{{nodes.creator}}"}""")
            .Node("output", "output")
            .Connect("input", "creator").Connect("creator", "results").Connect("results", "output").Build(),

        BehaviorBuilder.Define("agent-memory", "Create an agent and follow up")
            .On("AgentMemoryRequested")
            .Describe("Build a dedicated assistant, give it a fact, then ask a follow-up using the same memory. Example input: {\"name\":\"Ada\",\"favoriteColor\":\"violet\"}.")
            .Node("input", "input")
            .Node("creator", "agent", """{"mode":"spawn","instructions":"Remember information given in this conversation. Answer follow-up questions concisely.","prompt":"My name is {{input.name}} and my favorite color is {{input.favoriteColor}}.","lifetime":"run"}""")
            .Node("remembered", "agent", """{"mode":"collect","agent":"{{nodes.creator}}"}""")
            .Node("followup", "agent", """{"mode":"send","agent":"{{nodes.creator}}","prompt":"What is my name and favorite color?","wait":true}""")
            .Node("output", "output")
            .Connect("input", "creator").Connect("creator", "remembered").Connect("remembered", "followup").Connect("followup", "output").Build(),

        BehaviorBuilder.Define("company-lookup", "Find company address and email")
            .On("CompanyLookupRequested")
            .Describe("Research a company's official pages with an isolated Playwright browser. Returns address and public contact email with source URLs; missing details remain null. Example input: {\"companyName\":\"Tailscale\"}. Add \"website\":\"https://tailscale.com\" to disambiguate a company. Browsing can take up to three minutes.")
            .Node("input", "input")
            .Node("research", "agent", """{"mode":"web","operation":"company","company":"{{input.companyName}}","website":"{{input.website}}"}""")
            .Node("output", "output")
            .Connect("input", "research").Connect("research", "output").Build(),

        BehaviorBuilder.Define("web-research", "Research a public website")
            .On("WebResearchRequested")
            .Describe("Use a Playwright agent to browse public pages and return structured findings and sources. Example input: {\"task\":\"Extract the product name and main features\",\"url\":\"https://playwright.dev\"}.")
            .Node("input", "input")
            .Node("browser", "agent", """{"mode":"web","prompt":"{{input.task}}","startUrl":"{{input.url}}"}""")
            .Node("output", "output")
            .Connect("input", "browser").Connect("browser", "output").Build(),

        BehaviorBuilder.Define("welcome", "Personal greeting")
            .On("WelcomeRequested").Describe("Welcome someone by name. Example input: {\"name\":\"Ada\"}.")
            .Node("input", "input")
            .Node("greeting", "template", """{"template":{"message":"Hello, {{input.name}}!"}}""")
            .Node("output", "output")
            .Connect("input", "greeting").Connect("greeting", "output").Build(),

        BehaviorBuilder.Define("qualified-lead", "Qualify a lead")
            .On("LeadReceived").Describe("Keep leads scoring at least 70. Example input: {\"name\":\"Ada\",\"score\":85}.")
            .Node("input", "input", outputType: "Lead")
            .Node("qualify", "filter", """{"path":"input.score","operator":"gte","value":70}""", inputType: "Lead", outputType: "Lead")
            .Node("output", "output", """{"template":{"name":"{{value.name}}","score":"{{value.score}}","qualified":true}}""")
            .Connect("input", "qualify").Connect("qualify", "output").Build(),

        BehaviorBuilder.Define("contact-cards", "Map contact cards")
            .On("ContactsReceived").Describe("Project a contacts array into cards. Example input: {\"contacts\":[{\"name\":\"Ada\",\"email\":\"ada@example.com\"}]}.")
            .Node("input", "input")
            .Node("cards", "map", """{"path":"input.contacts","template":{"title":"{{value.name}}","email":"{{value.email}}"}}""")
            .Node("output", "output")
            .Connect("input", "cards").Connect("cards", "output").Build(),

        BehaviorBuilder.Define("invoice-total", "Sum invoice amounts")
            .On("InvoiceReceived").Describe("Sum decimal amounts without converting them to text. Example input: {\"items\":[{\"amount\":12.5},{\"amount\":7.5}]}.")
            .Node("input", "input")
            .Node("total", "aggregate", """{"path":"input.items","operation":"sum","field":"amount"}""")
            .Node("output", "output", """{"template":{"total":"{{value}}"}}""")
            .Connect("input", "total").Connect("total", "output").Build(),

        BehaviorBuilder.Define("order-summary", "Branch and join an order")
            .On("OrderReceived").Describe("Count items and sum prices in two branches, then combine their results. Example input: {\"items\":[{\"price\":10},{\"price\":15}]}.")
            .Node("input", "input")
            .Node("count", "aggregate", """{"path":"input.items","operation":"count"}""")
            .Node("total", "aggregate", """{"path":"input.items","operation":"sum","field":"price"}""")
            .Node("output", "output", """{"template":{"count":"{{nodes.count}}","total":"{{nodes.total}}"}}""")
            .Connect("input", "count").Connect("input", "total")
            .Connect("count", "output").Connect("total", "output").Build(),

        BehaviorBuilder.Define("priority-message", "Route a priority message")
            .On("MessageReceived").Describe("Accept urgent messages from a supported channel. Example input: {\"text\":\"Urgent: review needed\",\"channel\":\"chat\"}.")
            .Node("input", "input")
            .Node("priority", "filter", """{"all":[{"path":"input.text","operator":"contains","value":"urgent"},{"path":"input.channel","operator":"in","value":["chat","email"]}]}""")
            .Node("output", "output", """{"template":{"text":"{{value.text}}","priority":"high"}}""")
            .Connect("input", "priority").Connect("priority", "output").Build(),

        BehaviorBuilder.Define("timer-status", "Read a timer neuron")
            .On("TimerStatusRequested").Describe("Call the existing timer read capability through the same graph. Input: {}. This reads timer:example; it does not schedule a timer.")
            .Node("input", "input")
            .Node("timer", "call", """{"neuron":"timer:example","interface":"timer","method":"read","arguments":{}}""")
            .Node("output", "output")
            .Connect("input", "timer").Connect("timer", "output").Build(),

        BehaviorBuilder.Define("csharp-discount", "Calculate a discount in C#")
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
