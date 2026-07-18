# DigitalBrain

> **The operating system of new generation.** Talk to it; it writes the
> code, gates the tests, activates the behavior. Multiple private brains
> per machine — one per project, one per persona. Built on **DigitalBrain**,
> a proprietary substrate of **.NET Aspire**, **Microsoft Orleans**,
> **Microsoft.Extensions.AI**, and the **InoLang** interpreter.

## What it is

Two layers, one product:

- **DigitalBrain** — the substrate. The Kernel, the InoLang Interpreter, the
  Navigator + Cortex runtime, the Orleans bus, the Aspire host. Proprietary;
  invisible to InoLang authors. (Per [v3 D-A][v3-vision] this naming is
  permanent — no mass-rename, ever.)
- **DigitalBrain** — the operating system installed on DigitalBrain. The
  brand, the product, *digitalbrain.tech*. Open-source on the proprietary
  substrate. One running instance = **a Brain**. A user owns N brains.

You launch it with one command:

```pwsh
dotnet run digitalbrain.cs
```

Aspire spawns the kernel silo, the SDK silo, the authored-catalog silo,
and the Flutter client. The Flutter client opens on a **Constellation**
of your brains — one node per project / persona. Click a brain; the
camera flies in. You're inside it: a living graph of its neurons, a thin
**Tools Dock** (Ino · Creator · Settings), and whatever RFW surfaces the
neurons are currently emitting. You talk to **Ino**, the personal
assistant; Ino delegates work to the brain's Navigator, which runs it as
InoLang neurons over typed synapses against your real Gmail, your real
filesystem, your real calendar — locally, by default, forever.

## The canonical vision

The canonical, approved product vision is **[`docs/v5plan/VISION.md`](docs/v5plan/VISION.md)**
("DigitalBrain — Vision v5 (The Cut)"). v5 keeps the v4 product shape
and cuts the implementation by ~70% — fewer projects, one file per
neuron, no global catalog, UI declared in Ino. Companion v5 docs:

- [`VISION.md`](docs/v5plan/VISION.md) — the v5 invariants + the cut list
- [`INO.md`](docs/v5plan/INO.md) — the unified language (neuron + synapse + RFW + scenario in one file)
- [`DOMAINS.md`](docs/v5plan/DOMAINS.md) — GitHub-based install model, per-brain isolation
- [`SDK.md`](docs/v5plan/SDK.md) — the one place hand-written C# lives
- [`ROADMAP.md`](docs/v5plan/ROADMAP.md) — the v4 → v5 cut sequence (C1–C5)

v5 inherits every v4 decision it is silent on. The v4 product-shape
docs remain authoritative for what v5 has not yet rewritten:

- [`docs/v4/VISION.md`](docs/v4/VISION.md) — v4 north star, the five v4 invariants
- [`docs/v4/SHELL.md`](docs/v4/SHELL.md) — Constellation / Brain Scene / Tools Dock (unchanged in v5)
- [`docs/v4/ARCHITECTURE.md`](docs/v4/ARCHITECTURE.md) — multi-brain isolation mechanism
- [`docs/v4/LAUNCH.md`](docs/v4/LAUNCH.md) — the `dotnet run digitalbrain.cs` story
- [`docs/v4/NAMING.md`](docs/v4/NAMING.md) — DDD vocabulary
- [`docs/v4/RFW.md`](docs/v4/RFW.md) — RFW surface contract, lock states
- [`docs/v4/ROADMAP.md`](docs/v4/ROADMAP.md) — the v3 → v4 epics (superseded for C1–C5 scope)

v3 stays load-bearing for the InoLang language freeze, the Kernel-private
Interpreter, the spec-first runtime invariant, and the permanent
two-layer naming split.

## The v5 invariants (don't relitigate)

These narrow v4 where the implementation needed cutting:

1. **V5-1 One file per behavior.** A neuron = **one `.ino` file**
   containing its contracts, handlers, RFW surface, and scenarios.
   No more `.cs` + `.feature` + `.Steps.cs` triplet.
2. **V5-2 One message type.** `Signal` is gone as a distinct concept;
   it's just a synapse with broadcast routing.
3. **V5-3 No global catalog.** `MapCatalog` is deleted. Ports resolve
   lazily at activation; unresolved references fail soft.
4. **V5-4 UI is data.** Every neuron declares its RFW surface in its
   `.ino` as an `rfw:` block. The Flutter shell is a generic renderer.
5. **V5-5 Domains are repos.** A domain is a GitHub repo of `.ino`
   files. `digitalbrain install <owner>/<repo>` clones it per-brain.

These extend the v4 invariants (one process / Constellation only /
brain-isolated state / default RFW / Idle-Busy-Modal lock states),
which remain in force unchanged.

## The PoC scenario (in v4 form)

You run `dotnet run digitalbrain.cs`. The Constellation opens with your
five brains — *Personal*, *Acme Client*, *Side Project*, *Family*,
*Research*. You click *Acme Client*. Camera flies in. You open **Ino**
and say *"summarize this week's client emails and add a row to the
status sheet"*. Ino streams a planning RFW while **Creator** (invoked
under the hood) authors `Acme.WeeklyDigest.ino`, runs its scenario red,
then green, then activates it. A new neuron pulses into the graph; an
RFW card flies out of it into Ino's right pane showing the summary; one
click pushes the row to Sheets. You hit `Esc` to the Constellation,
switch to *Side Project*, repeat. Each brain has its own memory, its
own installed bundles, its own OAuth tokens. Signed in, you can publish
the new neuron to the **Marketplace** (free for local; **Global Brain
access requires login / brain-sync**; revenue is SDK access + a 20%
commission on shared software).

## Stack

| Concern | Library | Notes |
|---|---|---|
| Launch entry | .NET 10 file-based app | `dotnet run digitalbrain.cs` (`docs/v4/LAUNCH.md`) |
| Composition root | .NET Aspire 9.4+ | One `DistributedApplication.CreateBuilder` per process |
| Distributed actors | Microsoft Orleans 10 | Each silo class hosts a slice of the catalog; brains share the cluster but namespace grains by `BrainId` |
| LLM / embedding / voice | Microsoft.Extensions.AI 1.0.3+ | `IChatClient`, `IEmbeddingGenerator`, `ISpeechToTextClient` — wrapped by `LlmModelNeuron`, `GenerateEmbeddingNeuron`, `TranscribeVoiceNeuron` |
| Local voice-to-text | Whisper.net 1.9.0+ | CUDA 13/12 with CPU fallback; Vulkan / Metal / CoreML |
| Authoring language | **InoLang** | The user-authored surface; behavior + tests in one document. Spec frozen at v3 E-ABI |
| Engineering language | C# | Kernel + Boot + Brain shell + SDK connectors (`DigitalBrain.SDK`) |
| BDD (legacy / removed in v4) | Reqnroll 3.3+ | Retained transitionally for slice-A neurons until E-INO completes for them |
| Google APIs | Google.Apis.Auth.OAuth2 + Google.Apis.Gmail.v1 + Google.Apis.Calendar.v3 | Tokens namespaced by `BrainId` (`docs/v4/ARCHITECTURE.md` §4) |
| Storage | SQLite (Microsoft.Data.Sqlite) | Per-brain databases under `%LocalAppData%\DigitalBrain\brains\{brainId}\databases\` |
| Client | Flutter desktop/web + `rfw` | gRPC to the gateway; Constellation + Brain Scene + Neuron Scene; no Home (v4 V4-2) |

## Project layout

The full tree is in [`docs/DIGITALBRAIN_RESEARCH.md` § 19](docs/DIGITALBRAIN_RESEARCH.md#19-final-project-tree).
Quick orientation:

```
kernel/                          # platform host, core engine, bootstrapper, and authored-catalog silo
├── DigitalBrain.AppHost/             # Aspire composition root (Kernel dev entry — end users use digitalbrain.cs)
├── DigitalBrain.Hosting/             # AddDigitalBrain() — the v4 fluent launch surface (E-LAUNCH)
├── DigitalBrain.ServiceDefaults/     # AddServiceDefaults / AddDigitalBrainDomain / AddDigitalBrainClient
├── DigitalBrain.Core/                # Synapse, Neuron, IHandle, INeuronMetadata, capability markers — no Orleans runtime
├── DigitalBrain.Core.Hosting/        # Orleans grains, Roslyn compiler, registry, BrainId key prefix (E-MULTIBRAIN)
├── DigitalBrain.Core.SourceGen/      # Source generators + v4 naming analyzer (E-NAMES)
├── DigitalBrain.Kernel/              # Creator, Navigator, Gateway, Introspector, Marketplace, BrainRegistry, Ino
├── DigitalBrain.Boot/                # L4 bootstrap floor + Genesis (DigitalBrain.Genesis.ino)
├── DigitalBrain.NeuronTesting/       # in-process test harness for neurons
└── DigitalBrain.Domains.Dynamic/     # the authored-catalog silo — per-brain interpreted neurons
sdk/                             # native C# connectors & SDKs (DigitalBrain.SDK)
├── DigitalBrain.SDK/            # Unified SDK C# assembly containing core connectors (Ai, Aspire, Stripe, Telegram, Postgres, etc.)
├── DigitalBrain.SDK.Contracts/  # Consolidated synapse, neuron, and signal contracts
├── DigitalBrain.SDK.Mcp/        # Model Context Protocol integration
└── digital_brain_sdk_flutter/   # Flutter client-side Dart SDK interfaces
inolang/                         # InoLang parser, interpreter, and tests
UI/flutter/                      # the open-source Brain shell (DigitalBrain.Brain) — Constellation, Brain Scene, Neuron Scene
bundles/reference/               # reference bundles (TripRadar, …) — was samples/ in v3
digitalbrain.cs                  # the single-file launch entry (E-LAUNCH)
```

InoLang authored units may reference each other's `.Contracts` but
**never** each other's silo project. Cross-bundle calls go through the
Navigator by synapse type.

## Getting started

Prerequisites:

- .NET 10 SDK (or whatever `global.json` pins)
- Docker Desktop (only for `--profile=product` / `production`; `local`
  uses in-memory clustering)
- A Google Cloud OAuth client ID (Desktop App type) with Gmail and
  Calendar APIs enabled — drop the JSON into `secrets/google-client.json`
- An OpenAI or Anthropic API key in user secrets
  (`dotnet user-secrets set OPENAI_API_KEY ...` on the launch project)

### Run as a product user

```pwsh
git clone https://github.com/brainruntime/DigitalBrain.git
cd DigitalBrain
dotnet restore
dotnet run digitalbrain.cs                # boots the whole product
```

The Aspire dashboard opens at `https://localhost:17005`. The Flutter
client launches as an Aspire resource at `http://localhost:5800`. First
launch auto-creates a "primary" brain.

### Run as a Kernel developer

For inner-loop work on the Kernel itself (where you need per-resource
`rebuild`, log inspection, etc.), use the AppHost directly:

```pwsh
aspire start                                # boots the AppHost cleanly
aspire stop                                 # stops it + child resources
```

> **Do not** run `dotnet run --project src/DigitalBrain.AppHost` directly.
> Stopping it (Ctrl+C, IDE stop, killed terminal) leaves the AppHost
> process *and* the `orleans-redis-*` container running, and the next
> session inherits the orphans. Use `aspire start` / `aspire stop` for
> the full lifecycle. Never use `tasklist` / `taskkill` / `Get-Process`
> to inspect or stop DigitalBrain silos — use `aspire stop`, or the Aspire
> MCP tools (`list_apphosts`, `list_resources`, `execute_resource_command`).

## Tests

All 440+ tests must be executed **sequentially** and without
stage-filtering flags like `@stage:fast`, `@stage:integration`, or
`@stage:e2e`.

Run the full suite sequentially from the root:

```pwsh
dotnet test --max-parallel-test-modules 1
```

### Orleans Port Contention Fix & Test Parallelization Rules

- **Orleans Port Contention Fix**: Orleans uses loopback clustering
  ports (e.g., 11111, 30000) for local silo communications. When tests
  run in parallel, multiple test host processes spin up Orleans silos
  simultaneously and compete for the same loopback ports, leading to
  port collisions, connection failures, and flaky test runs. Running
  tests sequentially avoids this contention.
- **Global Test Parallelization Constraint**: xUnit test parallelization
  has been explicitly disabled globally at the assembly level to enforce
  single-silo execution isolation and prevent resource race conditions.
  This is defined in the assembly configurations using the following
  attribute:
  ```csharp
  [assembly: CollectionBehavior(DisableTestParallelization = true)]
  ```
  This attribute is non-negotiable and must exist in both:
  - `DigitalBrain.Test/AssemblyInfo.cs`
  - `kernel/DigitalBrain.Kernel.Tests/AssemblyInfo.cs`

`flutter test` is **not used**. Flutter UI assertions live in
`DigitalBrain.E2E.Tests` and assert on the RFW payload contract over gRPC,
not on rendered pixels. See [`docs/DIGITALBRAIN_RESEARCH.md` § 16](docs/DIGITALBRAIN_RESEARCH.md#16-testing--only-dotnet-test-no-flutter-test).

## Configuring providers

In `digitalbrain.cs`:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

await builder
    .AddDigitalBrain()
    .WithLlmProvider<OpenAI>()
    .WithLlmProvider<XAI>()
    .WithEmbedding<TextEmbedding3Small>()
    .WithVoice2Text<LargeV3Turbo>()
    .WithDefaultConnectors()                 // LocalFile, Sqlite, Google, ...
    .WithConnector<StripeNeuron>()           // optional extras
    .WithShell()                             // Flutter client
    .Build()
    .RunAsync();
```

To swap OpenAI for Anthropic, replace `WithLlmProvider<OpenAI>()` with
`.WithLlmProvider<Anthropic>()`. To go fully local on a 3060 Ti, swap
to `.WithLlmProvider<Ollama>()`. The neuron base classes don't care —
they take `IChatClient` from DI. Model selection is parametric: there
is one `LlmModelNeuron`, keyed by `ModelDescriptor` (see
[`docs/v4/NAMING.md` §4.2](docs/v4/NAMING.md)).

## Roadmap

The v4 epics (`docs/v4/ROADMAP.md`) slot into the v3 spine
([`docs/v3/VISION.md` §11](docs/v3/VISION.md)):

```
v3 spine (in-flight): E-ABI → E-INO → E-RUN → E-SDK
                                         │
                                         ▼
                                    ┌──────────────────────────┐
                                    │ v4 critical path          │
                                    │  E-MULTIBRAIN             │
                                    │     │                     │
                                    │     ▼                     │
                                    │  E-LAUNCH                 │
                                    │     │                     │
                                    │     ▼                     │
                                    │  E-NAMES                  │
                                    │     │                     │
                                    │     ▼                     │
                                    │  E-SHELL                  │
                                    │     ├── E-INO   (Ino app) │
                                    │     └── E-CREATOR         │
                                    │     │                     │
                                    │     ▼                     │
                                    │  E-SURFACE                │
                                    └──────────────────────────┘
                                         │
                                         ▼
                          v3 parallel tracks resume:
                          E1 Marketplace · E2 Memory · E3 TripRadar
                          E-IDENT · E-SET · E-BRAND · E5 · E6
                          E8 platform research · E9 thinking
```

The older v2-era milestones — **M1 STORE → M2 INBOX → M3 DIGEST → M4
VOICE → M5 INTENT → M6 SCRIBE** in [`docs/final_phase/`](docs/final_phase/README.md) —
are **superseded** by the spine above but retained for their operational
detail (test stages, conventions, the Gmail→SQLite reference loop).

## License

TBD.

[v3-vision]: docs/v3/VISION.md
