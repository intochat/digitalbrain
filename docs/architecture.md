# Architecture

DigitalBrain is an AI-native operating system built around ready-to-use durable neurons and typed
causal facts. It is designed as a common substrate on which modules supply domain vocabulary and
behaviors, once the approved install rail exists, compose that vocabulary into product logic. This
root URL is the stable architecture entry point: it distinguishes what is built from what is designed
and indexes the responsibility authorities below.

<ArchitectureMap />

## 1. The vision

DigitalBrain is not a generic agent framework or an application shell. Its kernel owns durable,
typed neuron mechanics; modules own domain vocabulary; and behaviors compose that vocabulary. This
index remains the canonical route for the architecture, status framing, and topic authorities.

## Authority index

- [Kernel and module model](./architecture/kernel-and-module-model.md) — the Built substrate,
  vocabulary, and explicit module selection.
- [AI and Tasks](./architecture/modules-ai-tasks.md) — Built module surfaces and the Designed
  supervised path.
- [Google and Salesforce integrations](./architecture/modules-integrations.md) — semantic
  capability roots, MCP admission, approval, and provider boundaries.
- [Time](./architecture/modules-time.md) — Built Countdown and designed schedule work.
- [Flutter and Memory](./architecture/modules-flutter-memory.md) — code/L0/L1 Flutter status,
  residual product topology, and Memory’s deliberate out-of-scope boundary.
- [Behaviors, registry, and discovery](./architecture/behaviors-registry-and-discovery.md) —
  Designed install/execution rail and explicit pre-rail parity.
- [Hosting, durability, and testing](./architecture/hosting-durability-testing.md) — durable
  hosting profile, journals, observability, and proof tiers.
- [Ratified rules and open deviations](./architecture/ratified-rules-and-open-deviations.md) —
  checklist, deviations, rejections, and build order.

## Status and ordinary build

“Built” means the named contracts and runtime exist and have the stated proof; “Designed” means a
ratified target is not claimed as shipped. The Behavior foundation is Built. The builder, worker,
capability broker, installation rail, and product Behavior execution remain Designed. The fixed
activation address/state/wire facts and pre-rail compositions remain explicit in the Behaviors
authority.

Ordinary repository validation uses the compiler, tests, and Git. It does not refresh CodeGraph;
removing the former floating CodeGraph build hook is the dedicated cleanup-042 change.

## Compatibility status synopsis

This concise index preserves the former root’s status-facing headings and proof vocabulary. The
linked authority owns the detailed rationale; this synopsis does not change a Built claim into a
product-topology claim.

<a id="_2-the-kernel"></a>

### The kernel

The kernel is Built substrate mechanics. See [kernel and module model](./architecture/kernel-and-module-model.md)
for causal facts, the capability exception, and the module model.

<a id="_3-the-module-model"></a>

### The module model

The module model is explicit selection and typed vocabulary. The modules below retain their stable
status lines; their detailed boundaries are in the linked authorities.

<a id="_4-the-modules"></a>

### The modules

<a id="_4-1-ai"></a>

### 4.1 AI — status

Status: Built

Direct `Respond` owns a protected `AgentSession`; supervised durable checkpoint work remains
Designed. Microsoft.Extensions.AI is the public conversation boundary, MAF types stay internal, and
AI-to-Tasks.Contracts preserves the one-way boundary.

<a id="_4-2-tasks"></a>

### 4.2 Tasks — status

Status: Built

<a id="_4-3-google"></a>

### 4.3 Google — status

Status: Built

Google remains a southbound semantic capability boundary.

<a id="_4-4-salesforce"></a>

### 4.4 Salesforce — status

Status: Built

Salesforce keeps human-approved proposal and approval evidence at the provider boundary.

<a id="_4-5-time"></a>

### 4.5 Time — status

Status: Built — Countdown only

<a id="_4-6-flutter"></a>

### 4.6 Flutter — status

Status: Built (first-vertical vocabulary + L0/L1 journal proofs + C# northbound UI edge + module-owned `Flutter.Aspire.Hosting` WithUiEdge/WithFlutterHost projection + **pure-Dart** headless host at `clients/digitalbrain_flutter` + Windows chrome in nested `clients/digitalbrain_flutter/shell/` (`shell/lib/main.dart` + `shell/windows/`) — **code and L0/L1 only**); Designed (full product chrome beyond key/title shell, product journal observation on IDigitalBrain, multi-principal IdP edge); **residual unproven:** live product AppHost topology (`aspire start` / `aspire run` Healthy for silo + `digitalbrain-ui` + Flutter host) — L2 today proves TestingAppHost silo **without** OS surface only; **not** Built-live

`WithFlutterHost()` = Desktop; `WithFlutterHost<DesktopHost>()` and
`WithFlutterHost<HeadlessHost>()` remain explicit alternatives. `WithFlutterHost()` / `<DesktopHost>`
has **no Auto** or silent Auto fallback.

<a id="_4-7-memory"></a>

### 4.7 Memory — status

Memory remains deliberately out of scope.

## 5. Behaviors and scripting

Status: Designed

`IBehavior` marks the Built foundation, but the Behavior builder, worker, broker, installation
rail, and product execution are Designed. Runtime behavior installation is designed and not yet built.
The fixed activation path uses `IDigitalBrainNeuron` / `DigitalBrainNeuron`; built OS compositions are
pre-rail helpers, not installed Behaviors. The rail begins only with a human-approved proposal.

### Registry and discovery

See the [Behavior authority](./architecture/behaviors-registry-and-discovery.md) for detail.

### Hosting and durability

The [hosting authority](./architecture/hosting-durability-testing.md) records the durable profile.
An AppHost build must not be read as a CodeGraph refresh.

### Testing

L1 uses the real three-silo DigitalBrainFixture; L2 uses the assembly-owned `DigitalBrainAppHostFixture<TAppHost>`
and method-scoped RunningAppHost. Resource checks use host.Resource("silo"), and cleanup never enumerates or kills processes by name.
Test chat setup uses ConfigureChatClient. `BehaviorNeuron` is the Neuron and its single-file program is not.
<!-- assembly-owned DigitalBrainAppHostFixture<TAppHost> -->

The bounded debts remain explicit: trusted cluster peer, Journal history is bounded, Effectively-once processing is also windowed,
FIFO per target, Delivery ordering, Broadcast addressing, handler **types**, timeline stream, and
AsClient. DevUI is not part of the current architecture. The named durable token remains protected.
The documentation host uses AddViteApp("website", "../../docs").

Built (OS compositions, pre-Behavior rail) means the samples are pre-rail helpers, not the install
rail and not installed Behaviors; AccountEnrichmentSurface is not Gmail.

## 9. Ratified rules

1. Follow the detailed [ratified rules and open deviations](./architecture/ratified-rules-and-open-deviations.md);
   no detailed rule is weakened by this index.

## 10. Still open, known deviations, and rejected

Ical.Net, the MAF Durable Extension, a model tier, and raw invoke remain rejected or open exactly as
the detailed authority records.

## Previous root-anchor map

The root URL remains authoritative. These former root anchors resolve to their responsibility
authority; headings retain their text at the destination.

| Former anchor | Authority |
| --- | --- |
| `#_1-the-vision` | this index |
| `#_2-the-kernel`, `#typed-requests-are-reified-as-causal-facts`, `#the-one-deliberate-exception` | [kernel and module model](./architecture/kernel-and-module-model.md) |
| `#_3-the-module-model`, `#namespaces-are-the-vocabulary`, `#selection-is-explicit` | [kernel and module model](./architecture/kernel-and-module-model.md) |
| `#_4-the-modules`, `#_4-1-ai`, `#_4-2-tasks` | [AI and Tasks](./architecture/modules-ai-tasks.md) |
| `#_4-3-google`, `#_4-4-salesforce` | [Google and Salesforce integrations](./architecture/modules-integrations.md) |
| `#_4-5-time` | [Time](./architecture/modules-time.md) |
| `#_4-6-flutter`, `#package-family-and-public-identity`, `#semantic-neurons-not-iflutter`, `#projection-model`, `#northbound-path`, `#module-owned-os-surface-composition-built-projection-l0-live-healthy-residual`, `#live-host-observation`, `#historical-recovery-map`, `#auth-edge`, `#contract-drift-guard`, `#testing`, `#still-open-do-not-implement-as-settled`, `#_4-7-memory` | [Flutter and Memory](./architecture/modules-flutter-memory.md) |
| `#_5-behaviors-and-scripting`, `#os-composition-before-the-rail`, `#_6-registry-and-discovery` | [Behaviors, registry, and discovery](./architecture/behaviors-registry-and-discovery.md) |
| `#_7-hosting-and-durability`, `#observability`, `#testing` | [hosting, durability, and testing](./architecture/hosting-durability-testing.md) |
| `#_8-known-limitations`, `#_9-ratified-rules`, `#kernel-and-modules`, `#ai-and-maf`, `#behaviors`, `#integrations-and-mcp`, `#tasks`, `#time-and-hosting`, `#flutter-and-os-surface`, `#_10-still-open-known-deviations-and-rejected`, `#still-open`, `#known-deviations`, `#rejected`, `#_11-build-order` | [ratified rules and open deviations](./architecture/ratified-rules-and-open-deviations.md) |
