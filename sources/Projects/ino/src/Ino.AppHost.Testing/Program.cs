using Ino.Aspire.Hosting;
using Ino.Llm.Xai.Models;

// Test-mode AppHost. Mirrors the silo set in Ino.AppHost so tests boot the
// production topology, but stamps Ino:Mode = Testing on every project so
// silo-side AddInoChatClients swaps the real xAI factory for the BDD-mock
// factory without the test fixture having to mutate ambient process state.
//
// Telegram + the marketing website + the cloudflared tunnel are deliberately
// absent — they're not part of the neuron-test surface and would otherwise
// drag npm install + dashboard prompts into the harness boot.
var builder = DistributedApplication.CreateBuilder(args);

var ino = builder.AddIno("ino")
    .WithLlm<Grok4FastNonReasoning>().AsFast()
    .WithLlm<Grok4FastReasoning>().AsBalanced()
    .WithLlm<Grok420>().AsReasoning()
    .WithVoiceToText<WebSpeechApi>();

builder.AddProject<Projects.Ino_Kernel>("kernel")
    .WithHttpsEndpoint(name: "kernel-http")
    .PropagateInoConfig(ino)
    .WithInoTestMode();

builder.AddProject<Projects.Ino_Identity>("identity")
    .PropagateInoConfig(ino)
    .WithInoTestMode();

builder.AddProject<Projects.Ino_Domains_Travel>("travel")
    .PropagateInoConfig(ino)
    .WithInoTestMode();

builder.AddProject<Projects.Ino_Domains_Taxi>("taxi")
    .PropagateInoConfig(ino)
    .WithInoTestMode();

builder.AddProject<Projects.Ino_Domains_Location>("location")
    .PropagateInoConfig(ino)
    .WithInoTestMode();

builder.AddProject<Projects.Ino_Domains_Reminders>("reminders")
    .WithReference(ino.Iaw)
    .PropagateInoConfig(ino)
    .WithInoTestMode();

builder.AddProject<Projects.Ino_Domains_Recall>("recall")
    .WithReference(ino.Iaw)
    .PropagateInoConfig(ino)
    .WithInoTestMode();

builder.AddProject<Projects.Ino_Domains_Genesis>("genesis")
    .PropagateInoConfig(ino)
    .WithInoTestMode();

builder.Build().Run();
