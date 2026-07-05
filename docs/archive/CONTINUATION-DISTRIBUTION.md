# CONTINUATION — Distribution Rail, Marketplace Trust & Self-Hosted Packs

Status: PROPOSED — owner decisions D-DIST1…D-DIST10 below. No implementation yet.
Implementation gate: **M0** (§6) — distribution never jumps the cleanup/identity queue.
Repo: `E:\brain`. Date: 2026-07-04.
Anchors: `docs/PRODUCT_VISION.md`, `docs/SYSTEM_DESIGN.md`,
`docs/CONTINUATION-CLEANUP-SIMPLIFICATION.md` (D-CL1…D-CL8, Waves D1–D4),
`docs/CONTINUATION-MULTIUSER-IDENTITY.md` (D-MU1…D-MU6, S1–S5), `CONTINUITY.md`.

The product has exactly two irreducible verbs: **create** new capability (neurons, synapses,
UI — as bundles) and **distribute** it across trust boundaries. Everything else in the repo
exists to serve those two verbs or is trash. This doc defines the end-state distribution
architecture, the trust rail, and the TDD spec for it. It adds **zero code today** — the rail
is built test-first, milestone by milestone, and it must *shrink* the solution (see §5), not
grow it.

---

## 0. Requirements less dumb (untangling "truly distributed")

"Truly distributed, anonymous, maybe blockchain" decomposes into four requirements with four
different verdicts:

| Requirement as stated | Origin | Verdict |
|---|---|---|
| Multiple customers, each with security/privacy isolation | owner, 2026-07-04 | **Real. Already owned by CONTINUATION-MULTIUSER-IDENTITY** (I1–I5, S1–S5). Not re-solved here. |
| Distribute new neurons/synapses/logic, covered by tests, across clusters | owner | **Real. This doc.** It is literally the product (PRODUCT_VISION §4.4). |
| Anonymous distribution | owner | **Dumb as stated.** Anonymous *code* is an anti-requirement for a security-focused system. Rewritten: consumer privacy is absolute; publisher identity is a **pseudonymous signing key** — accountable (same key signs every release) without being a passport. The npm model. → D-DIST3 |
| Blockchain for transparency | owner | **Dumb as stated.** The property actually wanted is "nobody — including the registry operator — can silently swap or retract a published bundle." That property is an **append-only transparency log** (Certificate Transparency / Sigstore-Rekor shape). Consensus/tokens add latency, complexity, and an immutable public ledger that *conflicts* with privacy law — the opposite of the positioning. → D-DIST4 |

**Orleans reality check (binding).** One Orleans cluster is one trust domain: membership,
streams, and the grain directory all assume a trusted network. There is no anonymous global
Orleans mesh and we do not want one. Topology: **many independent clusters** (one per
customer / self-hosted), federated **only through the bundle registry**. The unit of exchange
across a trust boundary is the **signed bundle — never a grain call, never a journal, never
user data.** Code moves between clusters; data never does. → D-DIST5

---

## 1. The dogfood law: kernel = capabilities, bundles = features

Extends D-CL1 to its end state. D-CL1 says *no new feature enters the Kernel as a neuron if
it can be a pack*. D-DIST1 finishes the sentence: **every feature — including first-party
integrations like Salesforce and Google — is a bundle on the rail.** The kernel is the first
customer of its own marketplace. First-party gets no private path: it gets a pre-pinned
first-party publisher key and pre-installation as seed bundles, riding the identical rail.

**Boundary test** (apply to any file): *delete it — if bundle loading, journaling, identity,
capability enforcement, or transport breaks, it is kernel; otherwise it is a bundle.*

### 1.1 Kernel-final inventory (post-M3 target)

| Stays kernel | Why |
|---|---|
| `DigitalBrain.Core` protocol (`INeuron`/`Synapse`/`IHandle<T>`, lineage, checkpoints) + rail contracts (§1.3 seams) | The protocol |
| Pack rail: `FoundryCompilation`, `PackAlcEmbodier`, `CapabilityGate`, `MarketplaceNeuron`, signing/trust | The rail itself |
| Journal + projections + tolerant-reader policy (D-DIST7) | Truth |
| Identity spine (`UserSessionNeuron`, `NeuronScope`, gateway resolution — MULTIUSER S2) | Trust boundary |
| Delivery: Gateway gRPC, `HomeFeedBus`/`SignalEgressBus` (filtered per S2), UiKit/RFW, Telegram transport | Transport |
| `OAuthFlowNeuron` + LLM client capability (`LlmResponderNeuron`-shaped provider) | Capabilities bundles consume |

| Leaves kernel → bundle | When |
|---|---|
| Charts / DB / tabular / upload | Already deleted (Wave D2, D-CL5); return as content bundles on demand |
| Salesforce (adapter + CRM neuron + its synapses) | **M3** — the dogfood proof |
| Google/Gmail (adapter + neurons) | M3+1, same mechanics |
| Local-OS SDK neurons (Shell/Winget/FileSystem/…) | Deleted (Wave D3, D-CL2); *if ever* wanted again, only as a capability-gated **system bundle** in local mode — never re-welded |

Bundle taxonomy: **seed** (first-party, pre-installed, first-party key), **integration**
(OAuth/API providers), **content** (charts, visualizations, domain logic), **system**
(privileged capabilities, local-mode only, Phase 2+).

### 1.2 The operating loop (how the running system generates vs. reuses)

1. User asks Ino for a capability the installed bundles don't cover.
2. System searches the registry first (**reuse before generate** — deletion applied to work
   itself). Hit → install via the trust rail (§3).
3. Miss → authoring loop (Phase 0 moat): empty bundle file → tests → green live-rendered UI
   in one `dotnet test` → sign → publish. The new bundle is immediately reusable by every
   other cluster.
4. Either way the capability arrives the same way: verified, sandbox-tested, capability-gated,
   activated. There is exactly one door into a running kernel.

Self-improvement is this loop with the system as author: it writes T1 rules (or T2 packs),
proves them in a simulation silo against journaled scenarios, activates version N+1. The
journal is the rollback. The system never mutates itself in place.

### 1.3 Extension seams (Core contracts a bundle may implement)

- `IHandle<T>` — general synapse handling (exists).
- `IOAuthProviderAdapter` — integrations (born in MULTIUSER S4; **must live in Core**, not
  Kernel, precisely so a bundle can implement it — prerequisite for M3).
- `ITurnContextProvider` — chat-context contributions (MULTIUSER S5).
- `ui:*` emission via UiKit vocabulary (exists).

Law: a new seam enters Core only together with a consuming bundle and a failing test that
forces it. No speculative seams.

### 1.4 Capabilities a manifest may declare (enforced by `CapabilityGate`)

`llm` · `http:{host}` (named-host allowlist, no wildcard egress) · `config:user` (own
`user:{userId}` scope only, per D-MU4) · `config:app:read` · `ui` · `journal:{synapse-type}`
(emit/consume only declared types) · `oauth:{provider}` · `schedule`. Undeclared capability
use = denied at runtime + logged, not just at review time.

---

## 2. Bundle anatomy — one format, every transport

A bundle is a signed `.dbrain` file (zip). Same artifact moves by git/file share (M1),
registry (M2), Telegram deep link (Phase 1 sharing). One format, multiple transports.

```
bundle.dbrain
├── bundle.json          manifest (signed subject)
├── rules/               T1 declarative content (InoLang-B)
├── src/                 T2 C# content (compiled by FoundryCompilation on the consumer)
├── tests/               MANDATORY — feature tests + steps (CTRF-parseable output)
└── signature            detached signature over the manifest + content hashes
```

```json
{
  "id": "digitalbrain.salesforce",
  "version": "1.0.0",
  "publisher": "key:3f9a…",
  "kernelApi": ">=1.0 <2.0",
  "tier": "compiled",
  "capabilities": ["oauth:salesforce", "http:login.salesforce.com", "config:user", "ui"],
  "synapses": { "emits": ["CrmQueryCompleted@1"], "consumes": ["InoRequest@1"] },
  "references": ["digitalbrain.crm-contracts@1"],
  "tests": { "entry": "tests/", "requires": "green" }
}
```

**Synapse contracts across bundles.** A synapse type used by exactly one bundle ships inside
that bundle. A synapse type shared between bundles lives in a tiny **contracts-only bundle**
(the npm `@types/*` shape) named in `references`. Core keeps only protocol + rail synapses.
Consequence: `FoundryCompilation` compiles each pack against **Core + its declared
references** — a per-manifest reference set replacing the deleted blanket `Demo.Contracts`
reference (this generalizes the R5 fix instead of re-creating it with the next demo).
Consequence for Step 3 of the cleanup: integration synapses eventually leave
`Core/Synapse.cs` entirely (§5.1).

**Mutability tiers** (D-DIST6):

| Tier | Content | Runtime mutability |
|---|---|---|
| T1 | Declarative rules (InoLang-B) | **Hot** — install/update/rollback at runtime, journaled |
| T2 | Compiled packs (C# via existing `FoundryCompilation` → `PackAlcEmbodier`) | Versioned activation: new version embodied, old deactivated; where ALC unload proves unreliable, rolling silo restart is the honest path (the kept kernel-self-update rolling-HA mechanism) |
| T3 | Kernel | Release-only, rolling HA. Never hot. |

---

## 3. The trust rail

```
author → test (green locally) → sign → transfer/publish → verify signature
      → sandbox test run (consumer) → capability approval (owner) → activate
      → update: same rail, next version → uninstall: journal survives (D-DIST7)
```

- **Sign:** publisher keypair; the fingerprint *is* the identity (D-DIST3). Consumers pin
  keys on first approval; a key change is a loud event, never silent.
- **Verify:** signature over manifest + content hashes. Tampered bytes fail closed before any
  compile.
- **Sandbox test run:** the bundle's own shipped tests execute in an isolated TestCluster
  process on the consumer (the Phase 0 authoring-loop infra pointed at an incoming bundle —
  **not** the deleted `Kernel/Sandbox/`; that was runtime isolation, which stays Phase 2/M4).
  **No green, no embodiment** (D-DIST2). Tests are the contract between publisher and
  consumer — this is what makes N=2 co-development with a second person safe before any
  registry exists.
- **Capability approval:** consumer (owner) approves the declared capability set once per
  bundle@major; `CapabilityGate` enforces it forever after.
- **Transparency log (M2):** every publish appended to a Merkle log; clients verify inclusion
  proofs; the registry cannot silently swap bytes for a published version (D-DIST4).
- **What M1 trust is NOT:** runtime isolation of malicious code. At M1/M2 the threat model is
  *trusted-but-fallible* publishers (you, the friend, first-party). Signature + tests +
  capability gate cover that. Hostile publishers are M4 (PRODUCT_VISION Phase 2), where
  isolation returns from the 10% ledger.

---

## 4. TDD spec — the rail *is* its tests

Law: no rail code without a failing test naming it first; no new project until a test cannot
compile without one (see D-DIST9 — through M2 that answer is "never").

**M1 — exchange without a registry**
- `Bundle_without_manifest_capabilities_is_rejected_at_load`
- `Tampered_bundle_content_fails_signature_verification_before_compile`
- `Bundle_signed_by_unknown_key_requires_explicit_owner_approval`
- `Bundle_shipping_no_tests_is_rejected`
- `Bundle_with_red_sandbox_tests_never_activates`
- `Activated_bundle_using_undeclared_capability_is_denied_by_CapabilityGate`
- `Bundle_emitting_undeclared_synapse_type_is_denied`
- `Second_publisher_key_bundle_installs_alongside_first_party_bundles`
- `Uninstalled_bundle_journal_replays_without_type_load_errors` (D-DIST7; the D-CL7 problem, solved as infrastructure)
- `Same_bundle_version_reinstall_is_idempotent`

**M2 — registry**
- `Publish_appends_inclusion_proof_to_transparency_log`
- `Client_detects_registry_swapping_bytes_for_a_published_version`
- `Gated_publish_rejects_bundle_that_fails_registry_side_checks`
- `Install_by_id_verifies_signature_against_pinned_publisher_key`
- `Key_rotation_for_known_publisher_requires_re_approval`

**M3 — dogfood (Salesforce exodus)**
- `Kernel_builds_with_zero_salesforce_references`
- `Salesforce_bundle_installed_from_registry_completes_oauth_via_shared_flow` (rides S4 `IOAuthProviderAdapter`)
- `Two_user_cross_contamination_suite_green_against_salesforce_as_bundle` (the S3 suite re-run against the bundle)

**Cross-cutting invariant tests (extend the Step-5 architecture guards)**
- `No_integration_specific_types_in_kernel_after_M3`
- `Every_installed_bundle_has_pinned_publisher_key_and_approved_capability_set`
- `No_cluster_egress_to_undeclared_hosts` (http capability allowlist)

---

## 5. Anti-bloat rules (binding)

1. **Zero new `.csproj` before M2.** M1 lands entirely inside existing projects: `Core`
   (manifest + rail contracts), Foundry (verify/sandbox gate), `MarketplaceNeuron`
   (install/approve), `SeedPacks` (first .dbrain producers), Tests. If M1 seems to need a new
   project, the design is wrong.
2. **Every milestone PR lists deletions alongside additions.** Net Kernel neuron count and
   project count may only go down or hold (post-D-CL4 ceiling: ≤31 projects).
3. **Reuse inventory** — extend, don't reinvent:

| Already exists (keep/extend) | Missing (build test-first) |
|---|---|
| `FoundryCompilation` / `PackAlcEmbodier` / `CapabilityGate` | Manifest v2 (capabilities + synapse contracts + tests entry + references) |
| `MarketplaceNeuron` + signing/trust gates | Publisher key pinning + owner approval flow |
| `SeedPacks` | `.dbrain` export/import (signed file) |
| Authoring loop + TestCluster infra (Phase 0) | Consumer-side sandbox test-run gate |
| Journal + projections | Tolerant-reader unknown-synapse policy (D-DIST7) |
| Gateway + Telegram delivery | Transparency log + registry endpoints (M2 only) |

4. **The registry is not a new service** (D-DIST8). `RemoteMarketplaceClientStub` (deleted,
   Wave D1/R4) is the cautionary tale: a stub for a repo that never existed. The registry is
   a DigitalBrain kernel instance whose product is the catalog — `MarketplaceNeuron` exposed
   through the existing gateway. Zero new deployables, zero new repos; the marketplace
   dogfoods the kernel.
5. This doc itself follows the rule: it authorizes tests, not scaffolding.

### 5.1 Further shrink unlocked by the rail (beyond D-CL targets)

After M3: `Kernel/Salesforce*`, `Kernel/Google*`/Gmail neurons and their synapses leave the
Kernel and Core. Projected: Kernel subdirectories ~14 (post-D4) → **~10**; `Core/Synapse.cs`
(post-split) loses all integration synapse files to bundles/contract-bundles — Core converges
on protocol + rail only. Update the cleanup doc's acceptance metrics when M3 lands.

---

## 6. Milestones

**M0 — gate (no distribution code before this).** Per D-CL6: MULTIUSER S1 → cleanup Waves
D1–D4 → MULTIUSER S2 landed, **and** D-CL7 explicitly decided (D-DIST7 builds on it). The
waves shrink the surface that must be signed, sandboxed, and capability-audited — cleanup is
distribution work.

**M1 — N=2 federation experiment (you + the friend).** Two independent clusters. Friend is a
*trusted* second party — the clientId trust model is documented acceptable exactly for this
(MULTIUSER pre-production note), so M1 needs S2, not full S5. Exchange `.dbrain` files via
git; each side's kernel refuses activation without signature verify + green sandbox run +
capability approval. Co-development protocol: bundle + its tests *are* the communication.
Acceptance: M1 test list green; a bundle authored on cluster A activates on cluster B with
its UI rendered end-to-end (the capstone Telegram → logic → `UiSurface` proof, executed
cross-cluster); zero new projects added.

**M2 — registry.** Gated publish (registry re-runs the same checks the consumer sandbox
runs), pinned keys, transparency log, catalog facets/search + web/Telegram deep-link sharing
(this *is* PRODUCT_VISION Phase 1). Registry deployed per D-DIST8 as a kernel instance.

**M3 — dogfood: Salesforce exodus.** Requires MULTIUSER S4 (adapter seam in Core). The ~30-line
`SalesforceAuthNeuron` adapter + CRM neuron + synapses move into `digitalbrain.salesforce`
(+ contracts bundle if any synapse is shared). Kernel keeps `OAuthFlowNeuron` + the seam.
Acceptance: M3 test list green; a fresh cluster acquires Salesforce *only* by installing the
bundle from the registry. Google follows with the same mechanics — and every future
integration is born a bundle, never a weld.

**M4 — open publishing (PRODUCT_VISION Phase 2).** Untrusted publishers: runtime isolation
(returns from the 10% ledger), review/reporting, BYO branded bots, embeddable surfaces.
Deliberately not designed further here — designing M4 before M1 ships is a just-in-case hedge.

---

## 7. Owner decisions

- **D-DIST1** — Dogfood law: kernel ships capabilities + seams; every feature including
  first-party integrations is a bundle on the rail. End state: zero integration-specific code
  in Kernel/Core after M3. Extends D-CL1. [PROPOSED]
- **D-DIST2** — Tests are the contract: a bundle without shipped tests, or with red tests in
  the consumer sandbox run, never activates. No green, no embodiment. [PROPOSED]
- **D-DIST3** — Publisher identity = pseudonymous signing key (fingerprint = identity), not
  anonymity, not real names. Consumer privacy absolute: nothing leaves a cluster without
  explicit opt-in. [PROPOSED]
- **D-DIST4** — Registry integrity = append-only transparency log (CT/Rekor shape), **not**
  blockchain. Revisit only if decentralized operation of the registry itself becomes a named
  requirement with a named owner. [PROPOSED]
- **D-DIST5** — Federation unit = signed bundle. No cross-cluster grain calls, no shared
  journals; user data and journals never leave their cluster. The registry is the only shared
  surface. [PROPOSED]
- **D-DIST6** — Mutability tiers: T1 declarative = hot; T2 compiled = versioned activation on
  the existing pack rail (rolling restart where unload is unreliable); T3 kernel =
  release-only rolling HA. [PROPOSED]
- **D-DIST7** — Uninstall/journal policy: generalize D-CL7 into rail infrastructure — a
  tolerant-reader journal projection that skips unknown synapse types with a logged count.
  One-time storage resets remain a pre-launch-only tool; bundle churn at M1 makes the
  tolerant reader mandatory. Built at M1. [PROPOSED]
- **D-DIST8** — Registry topology: a DigitalBrain kernel instance exposing `MarketplaceNeuron`
  via the gateway — not a separate service or repo. [PROPOSED — most opinionated call in this
  doc; challenge it]
- **D-DIST9** — Anti-bloat: zero new `.csproj` before M2; every milestone lists deletions with
  additions; project count ≤ the D-CL4 ceiling. [PROPOSED]
- **D-DIST10** — Sequencing: M0 gate as defined in §6; M3 additionally gated on S4.
  Distribution never jumps the cleanup queue. [PROPOSED]

Open (decide before M1): bundle id namespace convention (`publisher.name` vs flat);
`.dbrain` signature algorithm (reuse whatever the existing pack signing gates use — verify
before deciding); whether the friend's cluster runs from source or a published kernel image.

---

## 8. Risks / notes for the implementing session

- **D-CL7 is the sharp edge again, generalized.** Every bundle uninstall recreates the
  deleted-synapse-type replay problem. Do not ship M1 without the tolerant reader (D-DIST7);
  otherwise the N=2 experiment's natural churn corrupts confidence in journals.
- **`IOAuthProviderAdapter` placement decides M3's feasibility.** When implementing MULTIUSER
  S4, put the interface in `DigitalBrain.Core`, not the Kernel — a bundle cannot implement a
  Kernel-internal interface. One-line decision now, repo surgery later if missed.
- **Seeds/MCP tools are runtime references the compiler won't catch** (same warning as the
  cleanup doc): install/uninstall flows must grep-audit seed sources and MCP tool wrappers for
  grain keys and synapse type strings.
- `[GenerateSerializer]` + sequential `[Id(n)]` + concrete arrays on every new rail synapse
  (manifest records, install/approve/publish lifecycle synapses) — non-negotiable.
- Journals are never deleted (Core Law 2); rail lifecycle synapses must carry no secrets and
  no bundle payload bytes (hashes only).
- Capability `http:{host}` must be enforced at the egress HttpClient factory, not by
  convention — a bundle-provided `HttpClient` with an undeclared host is the test
  `No_cluster_egress_to_undeclared_hosts` exists to kill.
- Filesystem MCP quirk: never `list_directory` the repo root; targeted subdirectory listings +
  `read_multiple_files` batches.

## 9. Acceptance metrics

| Metric | Before (2026-07-04) | Target |
|---|---|---|
| New projects added by the rail through M2 | — | **0** |
| Integration-specific files in Kernel/Core after M3 | present (Salesforce, Google) | **0** |
| Kernel subdirectories after M3 | 25 (→ ~14 post-D4) | **~10** |
| Doors into a running kernel for new capability | several (welds, seeds, endpoints) | **1** (the rail) |
| Bundles installed via the rail in the live system | 0 | ≥ 3 (seed, salesforce, one content bundle) |
| Empty bundle file → green activation on a *second* cluster | — | measure at M1, then drive down |
| Transparency log inclusion verified in CI | — | green at M2 |
