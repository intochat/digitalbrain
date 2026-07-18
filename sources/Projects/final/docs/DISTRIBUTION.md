# DigitalBrain — Experience Distribution Architecture

OS3 (continuation per plan): N-1 arithmetic + UninstallBundle + requires check + Installed section landed (marketplace + grain + bindings steps); seed wiring; see OS-FROM-INO-PLAN.md for full + handoff. Core DistributionDynamicHandlers 20p/1f gate held throughout.
OS4: ino tools for install/uninstall/pin/move/run/describe/list_installed + live persona (installed facts from ListInstalled) now part of orientation; no new distribution N+1 but uses the OS3 uninstall inverse + ListInstalled. Gate held.

This is the core of the system. Design position, format, transport, trust, and the reasoning behind each choice.

## Thesis: distribution is replication of behavior with evidence, not file copying

Because everything is a neuron or a synapse, an experience is *fully described* by three things:

1. **Contract** — which synapse types it handles and emits. This is the only integration surface that exists, so compatibility is checkable by name.
2. **Behavior** — the `.ino` definition (and later: generated neuron source / references to built-in neurons).
3. **Evidence** — a trigger profile distilled from the author's durable journals: the manifest's `observedSynapses` (top synapse types from journal groups) plus the journal-size count in the `.ino`. The package proves it was *lived with*, not just compiled.

So the unit of distribution — the **`.brain` capsule** — is a zip:

```
weather-watcher-0.1.0.brain
├── manifest.json     id, name, version, description, author, createdAt,
│                     contentHash (SHA-256 of experience.ino), observedSynapses, files
└── experience.ino    name / version / desc / triggers / observed-synapses
```

(A separate `usage.json` was packed in early drafts; it was written but never read by any consumer, so v0 dropped it — evidence lives in the manifest. See `docs/DELETED.md`.)

Identity today is the content hash; the author field is the world/machine. The signature slot is reserved (see Trust ladder).

## The pipeline

```
live usage ──► PACK ──► PUBLISH ──► DISCOVER ──► INSTALL ──► GROW (N+1)
 (journals)  Packager   Marketplace   peer cluster   verify hash    BundleInstalled
             Neuron     Neuron        client (LAN)   + InstallBundle  broadcast
```

Every stage is a synapse (`PackExperience`, `ExperiencePacked`, `PublishToMarketplace`, `ExperienceListed`, `InstallFromMarketplace`, `ExperienceDownloaded`) — so ino, Creator, the TUI, tests, and future GUIs all drive distribution through the same nervous system, and the whole pipeline is journaled and replayable like everything else.

## Decisions and why

**Pull-first, push-optional.** The consumer's kernel fetches (`/install id peer`); the producer can also push (`/publish id peer`). Pull keeps the consumer in control (consent, quarantine, retries), works through the kernel's single Orleans gateway port, and matches how trust flows between friends. Push exists because "I'm sending you this" is a real social gesture.

**Orleans-native transport — the cluster client is the protocol.** A peer marketplace is just `IMarketplace` on another cluster, reached through `IDigitalBrain.Start(ConnectExisting + GatewayAddress)` → `IDigitalBrainClient.ClusterClient`. One serializer (version-tolerant Orleans codegen over the same `[GenerateSerializer]` contracts), one identity scheme (ClusterId = `digitalbrain-{world}`), zero parallel DTO surface to drift out of sync. `MarketplacePeer.ConnectAsync("world@host:gateway")` is the entire client. The Orleans timeline is also the one client transport for live `UiSurface` delivery to the TUI; kernel-side gRPC fanout (`SurfaceStreamService`) remains only where it earns its keep — Flutter.

**Marketplace and Packager are neurons, not services.** They're `DurableNeuron` grains with persistent state — so listings are domain state, publishing emits on the timeline, and a second account is just a different grain key / different cluster. Any connected cluster client — the local TUI, a peer brain, GlobalBrain later — calls the same `IMarketplace` grain. GlobalBrain is *the same neuron* activated in a public world.

**Accounts = worlds = clusters.** No user database. The root world and `example-world` already run as isolated Orleans clusters (own ClusterId/ports/journals) via the Aspire domain resource. "Download from another account" = another world's marketplace pulls through a peer cluster client (`world@host:gateway`). Inside a single cluster, domain-keyed brains (`IDigitalBrain("account-b")`) give the same isolation for simulation and tests.

**Idempotent, content-addressed installs.** Install-by-id is safe to repeat (`InstalledBundles` is a set); the hash pins exact content. Version bumps are new capsules; the listing keeps the latest per id (history is in the journal).

**Capsules carry no executable CIL in v1.** A `.brain` is data: contract + `.ino` + evidence. Installation activates *existing or generated-then-reviewed* neurons. Arbitrary code mobility only arrives behind the quarantine-world gate (Trust L3) — shipping a remote-code-execution protocol before sandboxing would betray the whole trust story.

## Trust ladder

- **L0 (now):** SHA-256 content hash verified on every install; mismatch → `ExperienceHashMismatch` telemetry, no install.
- **L1:** Ed25519 brain identity. Kernel boot generates a keypair (DigitalBrain.Kernel is already the identity layer); manifests gain `authorPublicKey` + `signature`; installs verify.
- **L2:** Sim-gate by default: install lands in a quarantine world (`StartWorldAsync("quarantine")`), the manifest's observed-synapses trigger profile replays as a smoke test, promotion to root only on green. The Creator's `ActionRunSimulation` gate is the seed of this.
- **L3:** Web of trust: pinned peer keys, listing endorsements as synapses, GlobalBrain reputation derived from install/usage telemetry that consumers opt to share.

## Discovery ladder

- **D0 (now):** explicit peer address (`/market world@host:gateway`). Honest and sufficient for "share with my friend."
- **D1:** UDP beacon: kernels broadcast `digitalbrain-market {world} {ip:gatewayPort}` on the subnet; `/market scan` lists neighbors. Peers persist in a `MarketplaceState` peer list (removed as unused in Phase 0; reintroduce when D1 lands — see `docs/DELETED.md`).
- **D2:** GlobalBrain registry — a hosted world running the same MarketplaceNeuron; LAN kernels sync listings up/down; monetization (OSS substrate, paid listings/curation) lives here.

## LAN reachability

Peers connect to the kernel's **Orleans gateway** (root world defaults to 30000, `example-world` to 30001). For cross-machine access set `DIGITALBRAIN_ADVERTISED_IP=<lan-ip>` on the kernel — the silo otherwise advertises loopback and remote cluster clients can never complete the handshake — and allow the gateway port through the firewall once. The peer spec `world@host:gatewayPort` carries everything the client needs: the world derives the ClusterId (`digitalbrain-{world}`) it must present, the rest is the static gateway endpoint.

## Failure semantics

Pack/publish/install are grain calls with honest exceptions surfaced to the TUI (`LastMsg`) and telemetry synapses on the timeline. Hash mismatch never installs. Remote fetch failures leave local state untouched. Everything that happened is replayable from journals — distribution debugging is journal reading.

## Private contract-only distribution (added for controlled API sharing)

A contract bundle is a `.brain` that carries *only* the integration surface (synapse vocabulary + `INeuron`/`IHandle<>`/`IEmit<>` wiring declarations) without any implementation payload (no `experience.ino`, no pre-shipped assembly activation).

**Shape (minimal extension of existing):**
- `ExperienceManifest` extended with `IsContractOnly` (default `false` for full backward compat with existing packages) + `ContractHandlers` (array of `ContractDeclaration(NeuronInterface, SynapseType, IsHandle)` — mirrors the `DigitalBrain.SourceGen.DispatchManifest.KnownContracts` shape exactly).
- Package entries: `manifest.json` + `contract.json` (the serialized decl array). `ExperiencePackageFormat.ContractEntry`.
- `ContentHash` is SHA-256 over the `contract.json` UTF-8 bytes (same verification path; `ino` entry absent and not required for contracts).
- No new top-level grain or `IContractMarketplace`. `IMarketplace`/`IPackager` + `InstallBundle` (extended with `IsContractOnly` + `ContractHandlers` payload) + `BundleInstalled` flow is reused. `PackAsync` gained trailing optionals + `PackContractAsync` convenience; `VerifyExtractInstallAsync` and install logic are conditional on the flag.
- On install: brain records in `NeuronState.ContractBundles` (separate from `InstalledBundles` so `ListActiveNeuronTypes` reports `contract-xxx` not `bundle-xxx` and no `ActivateExperiencesFor` prefix activation is attempted). `ListSubscribersAsync` generalized to add contract contributions (+1 per matching installed contract decl for the synapse) on top of the existing 1+dynamic+static math.
- Full experiences (.ino or awesome-style) continue exactly as before; old manifests default `IsContractOnly=false`; old packages without contract entry are treated as full.

This strengthens the Core Law: even a private shape-only distribution causes the same observable N+1 handler growth on the timeline for the declared synapses (proven exclusively via the Reqnroll/TestCluster simulation substrate + `SimulationNeuron` doubles that only implement the contract interfaces).

See `DistributionDynamicHandlers.feature` (the four new contract scenarios) + `CONTINUATION-PRIVATE-MARKETPLACE-CONTRACTS.md` for the executable specs and session notes. No UI filtering or real multi-silo peer contract tests required for this delta (sim covers the dispatch/journal/activation-guard paths).
