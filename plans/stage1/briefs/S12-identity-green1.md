# S1.2-GREEN-1 — workspace membership grains   (role: GREEN, part 1 of 2)

Report path: `plans/stage1/reports/S12-identity-green1.md`

## Ratified constraints (binding — from plans/RATIFIED-PRODUCT-DEFINITION.md §1.13)
- Orleans grains own workspace product state: membership, roles, invitations, audit.
- ASP.NET Identity (part 2, NOT yours) will own credentials/sessions at the Host boundary —
  therefore NO password/credential material and NO password crypto in any grain you build.
- Roles: Owner/Admin, Builder, Viewer. One deployed installation = one workspace (MVP).
- Durable commands must persist the actor stamp — `RequestContext` alone does not survive
  reminders/retries/restarts.

## Objective
Build the workspace-membership domain as neurons/grains in the Kernel (Core), TDD-first:

1. **Workspace neuron** (one per installation for MVP, e.g. grain `workspace:main`): holds
   workspace name + membership list. Verbs (as `RequestSynapse<T>` contracts where a reply is
   needed): add member, change role, remove member, read membership. Removing a member is
   durable and audited. Guard invariants: exactly one Owner minimum (the last Owner cannot be
   removed or demoted); only Owner/Admin may mutate membership; mutations by non-members are
   refused via `NeuronAuthorizationException` (kernel trap 4 — settled refusal, not retry).
2. **Member identity**: a `PrincipalId` (stable GUID) + username + role. No secrets. The GUID is
   the durable identity ASP.NET Identity will map to in part 2.
3. **ActorContext record** (contract type): `{PrincipalId, Username}` — the stamp that future
   durable commands will carry. Define it now in the appropriate contracts assembly and stamp it
   into the Workspace neuron's own audit entries so the pattern exists and is tested.
4. **Audit**: every membership mutation journals who did it (the acting `ActorContext`), what
   changed, when. Use the existing journal mechanics — do NOT invent a parallel audit store.

## Design discipline
- Follow existing Kernel patterns exactly: study `SynapseGraphNeuron` (a comparable
  registry-style neuron) for grain identity, alias, refusal, and test conventions FIRST.
- Contracts into the right contracts assembly (`DigitalBrain.Abstractions` — mirror where
  `ISynapseGraph` lives); implementation beside comparable Core neurons.
- Wire aliases: new aliases allowed under `db.workspace.*` (e.g. `db.workspace.add-member`).
  Names are the documentation — no `[Description]`, no XML boilerplate.
- NeuronTest-style tests for every verb + refusal path + last-Owner invariant + audit stamps.
  TDD: write the failing test first.

## Out of scope
HTTP, ASP.NET Identity, cookies, endpoints, chat changes, brain-factory changes (all part 2).
No new packages. No git.

## Definition of done
Gate passes (0 warnings, all tests green, no aspire); report lists the new contracts, aliases,
invariants, and test names.
