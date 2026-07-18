# DigitalBrain — Federation (the internet of agents)

> Status: **proposal / design memo**, not yet canonical. Builds on
> `docs/v5plan/VISION.md` (v5 "The Cut"), `docs/v5plan/DOMAINS.md` (install
> model), and the v4 multi-brain isolation (`docs/v4/ARCHITECTURE.md` §4,
> E-MULTIBRAIN). Where this memo and v5 conflict, v5 wins until this is
> promoted. Written 2026-05-30.
>
> This memo answers one ask: **"a new internet for agents where they can
> communicate and collaborate, and I want to see it all in the UI."**

---

## 1. TL;DR — federation is the cortex plus one hop

A single brain already routes **synapses** between **neurons** over its
gateway + Orleans cortex. Federation is **not a new protocol, a message bus,
or a parallel system.** It is the same synapse cortex extended by exactly one
new hop:

```
local:      ask Insurance.Triage              → local registry → local grain
federated:  ask Insurance.Triage@acme         → local miss → federation table
                                              → forward envelope to acme's gateway
                                              → acme executes → response by correlationId
```

Everything else in this memo — identity, exposure, trust — is the **minimum
surface needed to make that one hop safe**, and nothing more. A federated
synapse is the *same record and envelope* as a local one, plus an
origin/destination brain and a signature. **One concept, one wire, now across
the network.**

---

## 2. Question the requirement first (Musk step 1)

"A new internet for agents" sounds like new infrastructure. It is not.

- **A brain is already an addressable, isolated runtime context.** V4-3 gives
  every brain a `BrainId` and disjoint grain keys (`{brainId}::{neuronFqn}` —
  `DOMAINS.md` §6). The gateway already scopes every call by brain via the
  `x-brain-id` / `x-active-scope` request headers
  (`DigitalBrainGatewayService.GetRequiredBrainId`,
  `BrainScopeHelper.ActiveScopeKey`).
- **A synapse already carries its own request/response thread.**
  `SynapseMetadata` carries `correlationId` + `causationId`, so a response can
  find its way back across any number of hops without new plumbing.
- **The gateway already routes a `SynapseEnvelope` to a receiver neuron** by
  `type_name` (`SynapsePayloadRegistry`) and surfaces async results on the
  `BrainWatch` `watchSynapses` stream.

So the "internet of agents" is: **let a synapse whose target resolves to a
*remote* brain be forwarded to that brain's gateway, executed there, and the
response returned** — reusing the routing, scoping, correlation, and streaming
that already exist. The new code is the hop and the trust around it.

---

## 3. The five pieces

### 3.1 Brain identity & addressing

Each brain has a `BrainId` (local, opaque). Federation adds a **globally
resolvable, self-certifying handle**:

```
vlad-insurance@brain.vlad.dev          # name @ endpoint
```

The endpoint publishes the brain's **public key** (fetched once, pinned like
an SSH known-host — no central PKI). A neuron reference becomes *optionally*
brain-qualified:

```ino
using triage = neuron(Insurance.Triage)            # local (unchanged)
using triage = neuron(Insurance.Triage@acme)       # federated
```

Unqualified references stay 100% local — federation is purely additive and
opt-in. An alias `acme` resolves to a full handle via the brain's federation
table (see 3.5).

### 3.2 Exposure — the public surface (least privilege)

A brain exposes **nothing** by default. A single `federation.ino` manifest per
brain (the federation analog of `DOMAINS.md`'s `manifest.ino`) declares the
brain's public API and who may call it:

```ino
federation MyBrain
  identity vlad-insurance@brain.vlad.dev

  expose neuron(Insurance.Triage) to public
  expose neuron(Insurance.Quote)  to allow [acme@brain.acme.io]

  accept-from [acme@brain.acme.io, *.trusted-partner.io]
```

`expose` is to a remote neuron what `public` is to a C# member: everything not
exposed is `internal` to the brain and unreachable from outside, regardless of
ACL. This keeps V4-3 isolation intact — federation is the *controlled, declared
breach* of it, never an implicit one.

### 3.3 Transport — the one new hop

The gateway already has `Send(SynapseEnvelope)` and resolves the receiver. Add:

1. **Two envelope fields:** `origin_brain`, `destination_brain` (the cross-
   process analog of today's `x-brain-id` header).
2. **Forwarding:** when the target FQN is brain-qualified and the brain is
   remote, the local gateway stamps `origin_brain`, signs (3.4), and forwards
   the envelope to the remote brain's gateway over the existing gRPC contract.
3. **Execution & return:** the remote gateway verifies, sets its own
   `BrainScopeHelper.ActiveScopeKey`, routes into its cortex exactly as for a
   local synapse, and the response routes home by `correlationId` — the same
   mechanism a local `ask` uses.

No new transport, no broker. Gateway → gateway, peer to peer.

### 3.4 Trust — sign, verify, authorize

- Each brain holds a **keypair** (stored like OAuth tokens —
  DPAPI/libsecret/Keychain via the existing secret vault, never plaintext).
- Outbound federated envelopes are **signed** with the origin brain's private
  key.
- The receiver **verifies** against the origin's published public key and
  checks its `accept-from` allowlist.
- No match / bad signature → reject with a **`Federation.Rejected`** synapse
  (the cross-brain analog of V5-3's `Neuron.UnresolvedReference`) and a modal
  lock surface in the UI ("acme tried to call Insurance.Quote — allow?").

Day-one trust is **brain-level allowlist + signature**. Per-neuron capability
tokens, rate limiting, and revocation are deferred until the simple model
bites — same discipline as `DOMAINS.md` §8 deliberately not building a Trust
Layer prematurely.

### 3.5 Discovery — direct first, directory optional

- **Direct (v1):** you know the handle (`acme@brain.acme.io`), like typing a
  URL. The federation table is a list of handles + pinned public keys, managed
  by the `BrainRegistry` neuron.
- **Directory (additive):** reuse the `DOMAINS.md` §5 GitHub-topic trick. A
  brain may publish its handle + exposed-neuron catalog under the topic
  **`digitalbrain-brain`**; `digitalbrain search-brains` queries the GitHub API.
  No central server, no curated list — discovery is social, exactly like
  domains.

---

## 4. See it all in the UI — the constellation becomes the network

The parked `/constellation` (V4-2; E-MULTIBRAIN) is the federation view:

- **Local brain** = the central constellation (the Living Canvas of today).
- **Remote brains** = satellite constellations with a dashed boundary.
- **A federated synapse** animates as a comet crossing the gap between brains
  (reusing the `BrainWatch` `watchSynapses` stream + the existing graph comet
  renderer).
- **Remote exposed neurons** appear as dashed nodes inside the remote brain;
  you can `ask` them from the UI just like local neurons, subject to the ACL.

This is the literal "internet of agents" rendered: brains as nodes, federated
synapses as the traffic between them — and because it rides the same RFW +
BrainWatch path the Living Canvas already uses, **no new shell code per
brain** (V5-4 holds).

---

## 5. What it reuses (grounded in the repo)

| Need | Existing seam |
|---|---|
| Route a synapse to a receiver | `DigitalBrainGatewayService.Send(SynapseEnvelope)` + `SynapsePayloadRegistry` |
| Scope a call to a brain | `GetRequiredBrainId` (`x-brain-id` header) + `BrainScopeHelper.ActiveScopeKey` |
| Carry request/response across hops | `SynapseMetadata` `correlationId` / `causationId` |
| Stream traffic to the UI | `BrainWatch.watchSynapses` |
| Namespace state per brain | `{brainId}::{neuronFqn}` grain keys (`DOMAINS.md` §6) |
| Know about other brains | `BrainRegistry` neuron (extend with remote handles) |
| Store the keypair | the secret vault (`ISecretVault`, DPAPI/libsecret/Keychain) |

The new code is small and localized: envelope fields, the forward-on-remote
branch in the gateway, the `federation.ino` parser, sign/verify, and the
constellation comet for remote edges.

---

## 6. What NOT to build (the cut list)

- ❌ **No central federation server / broker.** Direct gateway→gateway, with
  an optional GitHub-topic directory. Same posture as `DOMAINS.md`.
- ❌ **No new message bus or wire protocol.** Reuse `SynapseEnvelope` + the
  existing gRPC gateway contract.
- ❌ **No global identity authority / PKI.** Self-certifying handles (publish
  your own public key at your endpoint), pinned like SSH known-hosts.
- ❌ **No per-neuron capability tokens on day one.** Brain-level allowlist +
  signature first; refine only when it bites.
- ❌ **No cross-brain distributed transactions.** A federated `ask` is a
  request/response, not a two-phase commit. If a remote call fails, you get a
  failure synapse — same as any local `ask`.
- ❌ **No always-on isolation breach.** A neuron is private unless
  `federation.ino` exposes it; a remote synapse is rejected unless signed by an
  `accept-from` brain.

> Apply the "add back ~10%" test: the one thing worth adding beyond the bare
> hop is the **`Federation.Rejected` → modal "allow?" surface**, so a human
> stays in the loop the first time a new brain calls in. That is the 10%.

---

## 7. Proposed invariant (V6-F)

**Federation is opt-in and least-privilege.** A neuron is unreachable from
outside its brain unless `federation.ino` exposes it; an inbound synapse is
rejected unless it is signed by a brain on the `accept-from` list. There is no
central registry — handles are self-certifying and discovery is direct or via
the GitHub topic `digitalbrain-brain`. A federated synapse is the same record +
envelope as a local one, plus `origin_brain` / `destination_brain` and a
signature.

**Relation to existing invariants:**

- **V5-2 (one message type):** a federated synapse is still just a synapse —
  one record, one envelope, now with two more header fields.
- **V5-3 (no global catalog / lazy resolution):** resolution extends naturally
  — local miss → federation table → remote resolve; an unreachable remote
  emits `Federation.Rejected`, the cross-brain twin of
  `Neuron.UnresolvedReference`.
- **V5-5 (domains are repos):** the federation directory is the same GitHub-
  topic pattern as domain discovery; no new infrastructure.
- **V4-3 (brain isolation):** federation is the *declared, signed, controlled*
  breach of isolation — never implicit.
- **V4-2 (Constellation is the only top-level screen):** the constellation is
  where federation becomes visible; satellites + comets, no new screen.

---

## 8. Slices (additive, each independently shippable)

| Slice | Deliverable | Depends on |
|---|---|---|
| **FED-1 — Identity & manifest** | `federation.ino` parser; brain keypair in the vault; handle + pinned-key table in `BrainRegistry`. | — |
| **FED-2 — The hop** | `origin_brain` / `destination_brain` on `SynapseEnvelope`; gateway forwards brain-qualified targets to a remote gateway; response returns by `correlationId`. | FED-1 |
| **FED-3 — Trust** | sign outbound, verify inbound, enforce `accept-from`; `Federation.Rejected` synapse + modal "allow?" surface. | FED-2 |
| **FED-4 — Constellation view** | render remote brains + federated-synapse comets; `ask` a remote exposed neuron from the UI. | FED-2, E-MULTIBRAIN |
| **FED-5 — Directory (optional)** | publish/query handle + catalog via GitHub topic `digitalbrain-brain`. | FED-1 |

Critical path: **FED-1 → FED-2 → FED-3**, then FED-4 (UI) and FED-5 (discovery)
in parallel. Ship FED-1→FED-3 against a hard-coded two-brain pair on one
machine before adding discovery or polish.

---

## 9. The end-to-end example

> Two brains: `vlad-insurance` (yours) and `acme` (a partner). Acme's brain
> wants your triage agent to classify a claim.

1. **Expose.** Your `federation.ino` declares
   `expose neuron(Insurance.Triage) to allow [acme@brain.acme.io]` and
   `accept-from [acme@brain.acme.io]`.
2. **Ask.** An `acme` neuron does `ask Insurance.Triage@vlad-insurance to
   "classify" with claim`.
3. **Hop.** Acme's gateway stamps `origin_brain=acme`, signs, forwards the
   envelope to `brain.vlad.dev`'s gateway.
4. **Verify & route.** Your gateway verifies acme's signature, checks the
   allowlist, sets the brain scope, and routes into your cortex — your
   `Insurance.Triage` grain runs exactly as for a local call.
5. **Return.** The response synapse routes back to acme by `correlationId`;
   acme's awaiting neuron resumes.
6. **See it.** On both constellations, a comet crosses the gap between the two
   brains as the synapse travels; your triage node pulses amber while it works.
7. **Reject path.** Had acme *not* been on `accept-from`, your gateway emits
   `Federation.Rejected`, and you get a modal "acme tried to call
   Insurance.Triage — allow?" — human in the loop.

Every arrow reuses an existing seam plus the FED-1→FED-4 additions. No broker,
no new protocol, no central authority.

---

## 10. Companion docs

- [`../v5plan/DOMAINS.md`](../v5plan/DOMAINS.md) — install model + the GitHub-
  topic discovery this memo reuses for the brain directory.
- [`../v5plan/VISION.md`](../v5plan/VISION.md) — the v5 invariants federation
  extends (V5-2, V5-3, V5-5).
- [`../v4/ARCHITECTURE.md`](../v4/ARCHITECTURE.md) — the V4-3 multi-brain
  isolation mechanism federation controls the breach of (E-MULTIBRAIN).
- [`MULTI_AGENT_LOCAL_LLM.md`](MULTI_AGENT_LOCAL_LLM.md) — within-brain agent
  collaboration (Phase 2), the local mesh federation generalizes across brains.
