# Distribution Algorithm Pass (Elon Steps 1–2) — "experiences ship as installable, self-joining packs"

**Date:** 2026-06-23
**Status:** Draft for review — an analysis/decision doc that drives the next build, not itself a build spec.
**Goal stated by the user:** Radically simplify software distribution. An *experience* = a bundle of
human-readable `.feature` scenarios (+ referenced C# scripts), everything-as-synapses, installable from the
marketplace, **joining the running cluster** the way `ino` does. Concrete proof target: a **Telegram bot that
needs only a token**, collected through a guided UI prompt (→ BotFather), optionally enabling a Flutter miniapp.
Apply Elon's Algorithm — question, then delete — before designing or building anything.

---

## Current state — what already exists (grounded in the code)

| Capability | Where | Real or stub? |
|---|---|---|
| `.feature`-described experiences | `awesome/*.feature` (CalendarSync, EmailProcessor, GmailDigest, SystemSelfUpdate, UIClosedLoop, SoftwareEngineering, …) | **Real** content; they are the "software description" |
| Pack model | `NeuroPack(Name,Version,OwnerId,IsPrivate,CommissionRate,Code,Description)` + `PublishToMarketplace`/`InstallFromMarketplace`/`NeuroPackInstalled` (`DigitalBrain.Protocol/Synapse.cs`) | **Real** records; journal-driven marketplace |
| Marketplace install | `MarketplaceNeuron.HandleAsync(InstallFromMarketplace)` (`SystemNeurons.cs:58`) | **Stub embodiment** — finds pack, fires `CommissionTaken`+`NeuroPackInstalled`, activates a generic `IGeneratedNeuron "generated-<name>"` with `ExperienceUsed`. **Never compiles `pack.Code`.** |
| Runtime code embodiment | **Code Foundry**: `FoundryRequest(Spec,Tier,AutoApply)`; `Tier.Run` = in-proc Roslyn → `AssemblyLoadContext` (`Foundry/InProcessAlcExecutor.cs`), `Tier.Deploy` = build → `SiloRestartRequested` → restart | **Real** engine, but **invoked separately** (`run_code_foundry` tool / `FoundryRequest`) — install does NOT call it |
| Self-update loop | `awesome/SystemSelfUpdate.feature` (SE/UI closed loops → publish pack → Aspire restart) | **Described**, partially wired via closed loops |
| Config / secrets prompt | Aspire `AddParameter("…", secret:true).WithDescription("…BotFather…", enableMarkdown:true)` (user's snippet) | Aspire **surfaces unresolved secret params** (the "popup") — local dev only |
| Webhook reachability | `WithCloudflaredTunnel(...)` (user's snippet) | Aspire integration — local dev only |
| MCP server | `DigitalBrain.Mcp` (Orleans client, **stdio** transport); Dart bridge **spawns it as a local child process** (`mcp_bridge_io.dart` `Process.start`), web = no-op stub; AppHost wires `DIGITALBRAIN_MCP_COMMAND=dotnet …DigitalBrain.Mcp.dll` | **Real local**, **no remote/web, not deployed** |
| Telegram host | **`e:/projects/ino/clients/Telegram/Ino.Telegram.Host` — NOT in this repo.** Here: only `sdk/telegram_bot_skeleton.dart` + an AppHost comment | **Absent here** |
| Cloud runtime | ACA, **no Aspire AppHost** (silo self-wires; Pass 2A added in-silo gRPC). No MCP, no Telegram, no Aspire param prompt in cloud | Aspire constructs are **local-only** |

---

## Step 1 — Question every requirement (trace each to a person + reason)

- **"Build a new distribution format (`.feature` bundles + C# scripts)."** — Challenge. The repo *already* treats
  `.feature` scenarios as the software description and `NeuroPack.Code` as the payload. Reason to keep a new format:
  none traced — "it would be nice" is not a reason. **Reuse `NeuroPack` + `.feature`; do not invent a format.**
- **"Install a pack → it joins the running cluster with real behavior."** — Required (this is the actual product).
  But today install is a **stub**: it never runs `pack.Code`. The real engine (Foundry `Tier.Run`) exists but isn't
  wired to install. **The one true gap.** Everything else is dressing.
- **"Scenario references (scenarios referencing scenarios)."** — Challenge / defer. No consumer needs it yet; YAGNI
  until one real experience needs to compose another.
- **"Collect the TG token via a custom UI popup."** — Challenge. Aspire **already** prompts for unresolved secret
  parameters with a markdown description (BotFather link). A bespoke popup duplicates that. **Delete the custom popup**
  unless/until the target is specifically the DigitalBrain Flutter UI in **cloud** (where Aspire's prompt doesn't exist).
- **"Ship via Aspire."** — Question hard. Aspire (`AddProject`/`AddParameter`/`WithCloudflaredTunnel`/param-prompt) is
  **local-dev only**; the **live system is ACA with no Aspire**. "Ship via Aspire" silently means "ship to a developer's
  laptop," not "ship to the running cloud brain." This contradiction must be resolved, not hedged.
- **"Telegram bot needing only a token."** — Keep as the concrete proof; but note the host (`Ino.Telegram.Host`) is
  **not in this repo** — it would be ported/rebuilt, or referenced from `e:/projects`.
- **"MCP as the interaction path."** — Already re-scoped: the Flutter bridge is local-process/desktop-only, so MCP-remote
  is a separate concern from this distribution pass. **Out of scope here.**

## Step 2 — Delete (then add back ~10%)

**Delete / do-not-build:**
- A new distribution format, registry, or manifest schema (reuse `NeuroPack` + `.feature`).
- A custom token-collection popup (reuse Aspire's secret-param prompt in local; defer cloud UI config).
- Scenario-reference machinery, marketplace economics polish, miniapp, multi-pack composition — all deferred.
- Any second cluster member / separate MCP app (ACA Orleans cross-app networking wall; not needed).

**Keep (the irreducible core):**
- `NeuroPack` + `.feature` as the unit; the marketplace publish/install journal flow.
- The **Code Foundry** as the embodiment engine.

**Add back ~10% (the missing wire):**
- **Connect marketplace install → Foundry embodiment.** When a pack is installed, its `Code`/`.feature` is actually
  embodied into the running silo via `Tier.Run` (in-proc) — replacing the stub `IGeneratedNeuron` activation. This is the
  single change that makes "install → joins the cluster" *true* instead of *staged*.

## The decision this pass forces

**Substrate: Aspire-local vs ACA-cloud.** Every "simple distribution" mechanism the vision leans on (param prompt,
cloudflared tunnel, `AddProject`) lives in **Aspire/local**. The shipped brain lives in **ACA/cloud** with none of it.
Two coherent resolutions:

1. **Distribution = local Aspire developer experience.** "Install an experience" means an Aspire-described resource +
   a `.feature` pack, configured by Aspire's secret-param prompt, tunneled by cloudflared. Cloud stays a separate concern.
   *Cheapest; everything needed already exists locally; matches the pasted snippet.*
2. **Distribution = runtime pack install into the live cluster (Aspire-independent).** "Install" = `InstallFromMarketplace`
   → Foundry `Tier.Run` embodies the pack in the running silo (local OR ACA), config delivered as a synapse/secret, webhook
   via the silo's existing ingress. *More on-vision ("just like ino"), Aspire-independent, but requires the install→Foundry
   wire and a non-Aspire config path.*

Resolution (1) optimizes the laptop; (2) optimizes the actual product. The Algorithm says delete the laptop-only hedge:
**(2) is the less-dumb requirement** — but it is bigger.

## Recommended minimal first vertical (to be spec'd next)

Smallest slice that proves the core, independent of Aspire-vs-cloud religion:

> **"Install a trivial `.feature` pack and watch it run in the already-running silo."**
> Wire `InstallFromMarketplace` → Code Foundry `Tier.Run` so a published pack's code is embodied in-process and begins
> handling synapses, replacing the stub `IGeneratedNeuron`. Prove with one tiny pack (e.g. an echo/greeter neuron) via a
> `TestCluster` test: publish → install → fire a synapse the pack handles → assert the journal shows the pack's response.

Only after that loop is real does the Telegram experience become "a pack + a token": a `.feature` describing the bot's
behavior as synapses, the token as the pack's one required config, and (substrate-dependent) Aspire's param prompt **or** a
synapse-delivered secret.

## Out of scope / explicitly deferred
Telegram host port, miniapp, scenario references, marketplace economics, MCP-remote/Dart-HTTP client, a custom config UI,
cloud webhook ingress — none are built until the install→embody loop is real and a substrate decision is made.

## Open questions for the next step
1. Substrate decision (1) vs (2) above — which does the first real vertical target?
2. Does the first vertical embody from **`pack.Code` (C# source)** or compile a **`.feature`** (needs a `.feature`→source
   step; the Foundry's `CodeGen` turns a *spec* into source via the LLM — is that the intended `.feature` path)?
