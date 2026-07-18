using Ino.Aspire.Hosting;
using Ino.Llm.Xai.Models;

var builder = DistributedApplication.CreateBuilder(args);

// LLM provider declarations. Each WithLlm<TModel> registers a secret Aspire
// parameter for that provider's API key — on first launch the dashboard
// prompts for any unfilled parameter, persists it to user-secrets, and forwards
// it to the silos as Ino:Llm:ApiKeys:<provider>. Same pattern as IAW's
// AddIAW().WithLLM<TModel>().
var ino = builder.AddIno("ino")
    .WithLlm<Grok4FastNonReasoning>().AsFast()
    .WithLlm<Grok4FastReasoning>().AsBalanced()
    .WithLlm<Grok420>().AsReasoning()
    .WithVoiceToText<WebSpeechApi>();

// Multi-silo localhost clustering — unchanged from prior AppHost. Each silo
// project configures UseLocalhostClustering() itself with a fixed siloPort.
var kernelSilo = builder.AddProject<Projects.Ino_Kernel>("kernel")
    .WithHttpsEndpoint(name: "kernel-http")
    .PropagateInoConfig(ino);

builder.AddProject<Projects.Ino_Identity>("identity")
    .PropagateInoConfig(ino);

// Each domain ships as its own NuGet-shaped Worker SDK silo. Per-domain Aspire
// resources give cross-domain trace filtering (own service.name + dashboard
// rebuild lifecycle) and align the runtime topology with the marketplace
// install model: a third-party domain's NuGet package contributes one silo
// here without changes to the Travel/Taxi declarations below.
builder.AddProject<Projects.Ino_Domains_Travel>("travel")
    .PropagateInoConfig(ino);

builder.AddProject<Projects.Ino_Domains_Taxi>("taxi")
    .PropagateInoConfig(ino);

// Location is substrate (no user-verb neurons) — provides per-user
// location memory that other domains' plans (taxi.ride-home, places.recall-
// with-contact, travel.materialize-routine, …) read via cross-silo
// IJournaledNeuronQuery<LocationVisited> calls.
builder.AddProject<Projects.Ino_Domains_Location>("location")
    .PropagateInoConfig(ino);

// Reminders is the first IAW→ino capability bridge (Phase 4 Slice B).
// RemindersNeuron : LlmNeuron<ReminderEvent> inherits IAW Agent.Scheduling
// — ScheduleJob/CancelJob/OnScheduledJobDueAsync come for free, ino just
// adds the neuron surface (reminders.set, reminders.cancel) and the
// journaled ReminderEvent shape on top.
//
// .WithReference(ino.Iaw) propagates the IAW substrate (Orleans, Blobs,
// Qdrant, LLM env block, GitHub token) so the silo's AddIAW() can wire
// up the [AgentState] mapper + ILocalDurableJobManager + IChatClient
// pipeline that LlmNeuron requires at grain-activation time.
builder.AddProject<Projects.Ino_Domains_Reminders>("reminders")
    .WithReference(ino.Iaw)
    .PropagateInoConfig(ino);

// Recall is the second IAW→ino bridge (Phase 4 Slice C). RecallNeuron is a
// pure-code canonical handler for RecallQuestion that wraps IAW's
// IMemoryLookup (Qdrant-backed semantic memory) keyed by the user's
// per-collection. Same .WithReference(ino.Iaw) handshake — gives the silo
// QdrantClient + IEmbeddingGenerator + IawMemoryProvider via AddIAW().
builder.AddProject<Projects.Ino_Domains_Recall>("recall")
    .WithReference(ino.Iaw)
    .PropagateInoConfig(ino);

// Genesis hosts the L1 self-improvement consumer (Phase 4 Slice E.2).
// CreatorNeuron reacts to L1Proposal broadcasts the kernel emits when
// MissedIntentTracker crosses its 3-occurrence threshold, drafts a
// Roslyn script body for the cluster, and registers it via
// INeuronRegistry — the next matching prompt routes through the
// shared RoslynPlan grain with no silo restart. v0.1 keeps the draft
// body deterministic (no LLM), so Genesis stays on the plain Neuron
// path without WithReference(ino.Iaw); upgrade to LlmNeuron + AddIAW
// when richer body synthesis lands post-acceptance.
builder.AddProject<Projects.Ino_Domains_Genesis>("genesis")
    .PropagateInoConfig(ino);

// Telegram bot — launches the Flutter mini-app via WebApp button, transcribes
// voice messages locally (Foundry Local Whisper), forwards text + transcribed
// voice to the kernel silo over gRPC.
//
// Cloudflared exposes the bot's local HTTP port at a public
// `https://*.trycloudflare.com` URL so Telegram can webhook to it without
// any manual tunnel setup. The same public URL is injected as
// `Telegram__WebhookUrl` and surfaces in the Aspire dashboard via the
// cloudflared resource's health-check description — clicking it opens
// the Flutter mini-app served from the bot's wwwroot. Same UX TripRadar.Bot
// has via cloudflared in tripradar/src/Aspire/Hosting/Bot/BotExtensions.cs.
//
// Pinning the bot to a fixed local port (instead of letting Aspire pick one)
// is required because cloudflared has to know the target port at process
// start. `isProxied: false` skips the DCP proxy so the project binds directly
// at TelegramBotPort — without it Aspire reassigns a random external port and
// cloudflared 502s because it's tunneling to a port nothing's listening on.
const int TelegramBotPort = 5500;

var telegramBotToken = builder.AddParameter("telegram-bot-token", secret: true)
    .WithDescription(
        "Create a bot and get the token from [@BotFather](https://t.me/BotFather) on Telegram",
        enableMarkdown: true);

// Marketing / docs site (VitePress). Lives at repo root /website and is wired
// into Aspire so `aspire run` brings the site up alongside the silos. The npm
// dependencies are restored by the InstallWebsiteDependencies MSBuild target
// in this project's csproj before the AppHost builds, so a clean clone +
// `aspire run` boots the site without a manual `npm install`. AddViteApp
// auto-creates an HTTP endpoint and passes the allocated port to the Vite
// dev server; .WithExternalHttpEndpoints() makes the dashboard URL clickable.
builder.AddViteApp("website", "../../website")
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.Ino_Telegram_Host>("telegram")
    .WithHttpEndpoint(port: TelegramBotPort, name: "telegram-http", isProxied: false)
    .WithReference(kernelSilo)
    .WaitFor(kernelSilo)
    .WithCloudflaredTunnel("telegram-tunnel", TelegramBotPort,
        "Telegram__WebhookUrl")
    .WithEnvironment("Telegram__BotToken", telegramBotToken)
    .WithEnvironment("Telegram__NgrokApiUrl",
        Environment.GetEnvironmentVariable("TELEGRAM_NGROK_API_URL") ?? "");

builder.Build().Run();
