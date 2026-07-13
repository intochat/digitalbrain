# 01 — Product North Star

This chapter states what DigitalBrain should be as a *product*, in language a user (not a compiler) understands, and tests that framing against the reference journeys. It is a proposal grounded in what the codebase already does well; where the current build contradicts it, findings are cited.

## Product promise (one sentence)

**DigitalBrain is a personal operating system that safely connects to your tools, does real work across them on your behalf, and shows you exactly what it will change before it changes anything — and can undo it.**

Not a chatbot with integrations. The defensible core is **governed action across connected capabilities**: preview, approve, journal, verify, reverse. That loop is the product.

## Operating-system framing (metaphor → contract)

The neuron/synapse vocabulary must resolve to concrete OS contracts, or it becomes marketing. The mapping the product should commit to:

| Metaphor | OS responsibility | Concrete contract |
|---|---|---|
| Neuron | Durable process/actor | An Orleans grain with a stable identity, a durable journal, a lifecycle, and an owner (tenant/workspace/principal). |
| Synapse | Typed IPC message | A serializable, versioned, principal-scoped message with a deterministic id and causal lineage. |
| INO | User-facing orchestrator / shell | The intelligence that turns intent into *previewed, approved, journaled* effect plans. |
| Connector | Device/capability driver | A uniform capability contract (auth + typed read + previewed mutation) any provider implements without kernel edits. |
| Pack | Installable behavior / signed package | A signed, capability-declaring unit of new behavior, human-approved before it runs. |
| Self-evolution rail | Package manager + change control | The single governed path by which the system modifies itself: propose → diff → risk → validate → approve → apply → journal → verify → rollback. |

The gap today: neuron/synapse are implemented (grains + messages), but **synapses carry no mandatory principal/tenant** (`core:ARCH-002`), **packs are not actually signed-and-verified** (`connectors:SEC-405`), and the **self-evolution rail does not enforce its own contract** (`kernel-runtime:SEC-050/051`). The metaphors over-promise relative to the contracts.

## Primary user and initial market

**Primary user:** an individual knowledge worker who lives in Gmail + a CRM (Salesforce) and does repetitive cross-tool work — triage mail, update records, chase follow-ups — and who will only trust automation that previews and is reversible.

**Initial market (beachhead):** solo operators and small revenue teams (founders, account execs, customer-success) for whom "read my inbox, prep the CRM update, show me before you save" is a daily, valuable, and currently-manual job. This is exactly the surface the two shipped connectors cover.

**Why this beachhead:** it needs *exactly one* connector pair to be lovable, it makes the preview/approve/undo loop the hero (not a footnote), and it is the shortest path to a journey where governed mutation is the visible value rather than hidden plumbing.

## Core user jobs

1. **"Read/summarize across my tools"** — connect on demand, get a minimized, cited result. (Gmail read path exists; `connectors`.)
2. **"Make this change for me, but show me first"** — propose a concrete diff, approve, apply, verify. (Salesforce preview→apply→verify exists and is the template; `connectors`.)
3. **"Do this every time X happens"** — turn a repeated action into an automation I approved and can watch/stop. (Automation rail exists but on the weak substrate; `foundry`, `kernel-runtime`.)
4. **"Learn a new trick"** — teach a new behavior, evaluate it, approve installing it, use it, roll it back. (Foundry/pack machinery exists but is unsafe/dead; `foundry:SEC-*`, `connectors:SEC-405`.)
5. **"Show me what you know, can touch, have changed, and are doing"** — an inspectable ledger of memory, grants, history, and in-flight work. (Journals + surface feed exist; no coherent user-facing control surface yet.)
6. **"Recover cleanly"** — after a crash or an uncertain external result, land in a safe, known state. (INO outcome-unknown machine exists; neuron-side rollback does not; `kernel-runtime:REL-103/ARCH-051`.)

## Principles

1. **Fail closed at every authorization and mutation boundary.** The default must be "deny / don't send." The INO gateway already does this (`ClosedInoToolGateway`); the neuron rail and grant checks do not (`connectors:ARCH-401`, `kernel-hosting:SEC-101`).
2. **Preview before mutate; verify after.** No external write without a human-readable diff and a post-write reconciliation. Salesforce does this; Gmail send does not (`connectors:PROD-400`).
3. **One rail for all self-modification.** Prompt, automation, pack, and code changes flow through the same propose→approve→journal→rollback path with the same evidence model.
4. **Least privilege, on demand, revocable.** Grants are per-capability, requested when needed, tenant/user-isolated, and revocable. Connector tokens honor this; the session/identity layer does not yet (`kernel-hosting:ARCH-101`).
5. **Everything reversible or explicitly not.** Reversibility is a first-class property of every action, surfaced to the user.
6. **The trusted core is small and never self-modifiable.** Kernel, identity, crypto, and the rail itself are outside what self-evolution can change (see [03](03-operating-system-assessment.md), [05](05-self-evolution.md)).

## Non-goals (explicit)

- Not a general LLM chat assistant; conversation is a control surface for governed action, not the product.
- Not an "agent that autonomously does things without asking." Autonomy is bounded by the approval rail; `TrustedAutoApply` (`foundry:SEC-303`) is a config-gated exception, not the default posture.
- Not a multi-connector integration marketplace *yet* — depth on two connectors and the governance loop beats breadth.
- Not in-process execution of untrusted generated code, ever (`foundry:SEC-302`).
- Not a distributed multi-tenant SaaS at this stage — but the identity/tenancy contracts must be built as if it will be, because retrofitting tenancy is the hardest migration.

## User mental model

The user should think: *"It's a careful assistant with hands. It can see my connected tools, it always shows me the exact change before it acts, everything it does is written down, and I can undo it or turn it off."* The words "neuron," "synapse," "grain," and "journal" should never appear in the UI; they are implementation. What the user sees: **connections**, **proposals**, **approvals**, **history**, **automations**, and **permissions**.

## Reference journeys (target) and where the build fails them

1. **Read Gmail on demand → minimized result.** *Target:* ask INO, connect if needed (least-privilege scopes), get a cited, minimized summary. *Status:* read path + scope minimization exist; OAuth lacks PKCE on Google (`connectors:SEC-400`); result minimization is real. **Mostly holds.**
2. **Update Salesforce → see exact change → approve → verify.** *Target the whole product is built to make lovable.* *Status:* preview→apply→verify with conflict/verification states exists — **this journey holds and is the template.** (`connectors`.)
3. **Propose an automation → approve → observe.** *Status:* automation define/remove flows exist through the rail, but the rail authenticates nothing and the automation `ScriptRunner` gate is a no-op (`foundry:SEC-301`); observation surface is thin. **Partially holds; unsafe.**
4. **Teach a behavior → evaluate → approve install → use → roll back.** *Status:* Foundry generates code and stages proposals, but execution is in-process full-trust (`foundry:SEC-302`), packs are unsigned/unverified (`connectors:SEC-405`), and rollback doesn't restore (`kernel-runtime:ARCH-051`). **Does not hold safely.**
5. **Add a new connector without touching kernel orchestration.** *Status:* impossible today — `IConnector` is auth-only and provider names are hardcoded in kernel logic (`connectors:ARCH-400/401`). **Does not hold.**
6. **Inspect what it knows/can access/changed/is doing.** *Status:* journals + surface feed provide the raw material; no unified, user-legible control surface exists. **Materials present, product absent.**
7. **Recover from a crash / uncertain outcome.** *Status:* INO side has a real lease/fence/outcome-unknown machine (**holds**); neuron/self-evolution side has non-retriable half-applies and non-restoring rollback (**does not hold**). **Split.**

## Minimum lovable product (MLP)

The smallest thing that is genuinely lovable and defensible:

> **INO, over Gmail + Salesforce, that reads across both, proposes concrete CRM changes with a human-readable diff, applies them only after approval, verifies the result, journals everything, and lets the user see connections, history, and permissions and revoke them — with recovery from uncertain outcomes.**

This is journeys 1, 2, 6, and 7 done well, on the INO/V2 substrate, with the neuron self-evolution machinery **switched off by default** until it is lifted onto the same trust model. It requires *deleting and disabling* more than it requires building — which is the fastest path to a coherent, trustworthy v1. Self-evolution (journeys 3–5) is the *second* increment, gated on the rail hardening in [05](05-self-evolution.md).

## What stays invisible

Grains, synapses, journals, checkpoints, log-consistency, Orleans, Aspire, gRPC, the RFW widget dictionary, and the entire self-evolution apply-handler registry are implementation detail. The user's surface is connections, proposals/approvals, history, automations, permissions, and a plain-language "what are you doing right now."
