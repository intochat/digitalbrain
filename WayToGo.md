You are the senior architect and hands-on refactoring lead for:

E:\intochat\digitalbrain

Your mission is to perform a forensic architecture review, identify contradictions and accidental complexity, propose a coherent target architecture, and produce an ordered strangler-style refactoring roadmap.

DO NOT begin implementation during the first pass. Do not delete or “clean up” anything until the architecture review and migration sequence are approved by Vlad.

## Current situation

The repository has undergone multiple overlapping refits, deletions, owner amendments and documentation rewrites. Do not assume any document, report, branch name or previous architectural decision is automatically correct.

At the time this prompt was written:

- `master` was clean at `3e330e7a`.
- `GROK.md`, `UNIFIED-ARCHITECTURE.md`, `INTERCONNECT-REVIEW.md` and `scripts/gate.ps1` had been deleted from `master`.
- `CLAUDE.md` still referenced those deleted files and commands, so it is internally stale.
- Historical Stage-1 reports describe behavior that may no longer match `master`.
- Current source is the factual description of present behavior, but it is not automatically the desired architecture.
- A real Aspire/Flutter failure exposed a split identity design:
  - Flutter started successfully but displayed `not connected`.
  - Kernel returned 401 for chat, authorization, topology and graph requests.
  - The direct Azurite `identityusers` table was empty.
  - Development loopback auth only impersonates an existing bootstrap Owner; it does not create one.
  - Flutter opens the streams once and does not reconnect after the 401.
  - The northbound MCP application connects directly to Orleans with a fixed operator actor, so MCP success does not prove Flutter/Kernel HTTP authentication works.

Verify all of this against the current repository and runtime. Do not blindly repeat it.

## Authority order

Use this order when sources conflict:

1. The explicit owner directives in this prompt.
2. The product goals and invariants below.
3. Current production source as evidence of actual behavior.
4. `plans/RATIFIED-PRODUCT-DEFINITION.md`, after identifying contradictions.
5. Current README/CLAUDE documentation.
6. Historical briefs, reports and deleted-file history as evidence only.

Record every conflict. Do not silently choose whichever source makes implementation easier.

## Owner directives

These are binding:

- Work directly yourself. Do not orchestrate Grok or delegate the review.
- Preserve `DigitalBrain.Modules.Salesforce.Contracts`. It permanently owns Salesforce neuron and synapse contracts.
- Wire aliases are permanent. Do not rename existing `db.*`, `ui.*`, `chat.*`, `probe.*` or other persisted aliases.
- Do not restore the deleted central test project.
- Do not run `dotnet test`, a test executable, `flutter test`, or any automated test suite.
- A proper testing architecture will be designed later with tests owned by each module.
- Do not introduce new packages without an explicit grant.
- No handwritten partial classes. Source-generated partial wire interfaces may remain when required.
- When a class becomes complicated, decompose it into focused collaborators behind a small interface.
- `DigitalBrain.Scripting` remains separate from Kernel projects. It connects to the cluster as a client and creates/runs single-file C# applications through Roslyn/file-based-app tooling.
- Never delete code merely because it looks unused. Prove its runtime and product role first.
- Do not delete or reset Azurite volumes or any persisted user/product state.
- Vlad is the merge authority.
- Do not create/switch branches, commit, push or merge during the audit stage.
- If Aspire is running, use it read-only for evidence. Do not build while AppHost is running.
- Before any later build, stop AppHost and all DigitalBrain processes cleanly.

## Product north stars to validate

Do not assume the current architecture satisfies these:

- A neuron is the canonical durable owner of product state and invariants, with persistence hidden behind the neuron runtime.
- A synapse is durable domain vocabulary, not a transport DTO or infrastructure command disguised as product vocabulary.
- Each piece of state has exactly one canonical owner.
- Host applications adapt transports, authentication and runtime composition. They must not become competing stores of product truth.
- Modules expose stable Contracts separately from implementation and optional Aspire hosting.
- Interfaces should form deep modules: small caller-facing interfaces hiding substantial implementation and invariants.
- Avoid shallow pass-through adapters, duplicated models, duplicated catalogs and speculative seams.
- One installation currently represents one workspace for approximately 100 users.
- User chat, integrations and credentials are principal-private unless explicitly published to the workspace.
- Connection means an in-brain graph relationship. Integration means an external account/provider.
- HTTP/SSE disconnects never decide whether durable work completes.
- Flutter is a resumable projection, not the owner of conversation state.
- Local Aspire development should start into a usable authenticated Flutter application on a clean local store without a manual bootstrap/login ceremony.
- Production must retain explicit secure bootstrap/login behavior.
- Raw passwords must never enter synapses, journals, logs or client-accessible grain interfaces.
- MCP, Flutter and every other ingress must resolve the same authenticated principal model. A fixed operator identity is not an acceptable substitute for user identity.

## Stage A — forensic review only

### 0. Reconcile reality

Before reasoning about architecture:

1. Print the current branch, HEAD, worktree status and recent commits.
2. Identify the merge-base and meaningful differences between `master` and `stage1-stabilize-strangle`.
3. Inventory deleted governance/build files that current docs still reference.
4. Do not modify or discard an existing dirty worktree.
5. Inventory every `.csproj`, `.slnx`, Flutter package and project reference.
6. Inspect current Aspire resources if an AppHost is running:
   - resource graph;
   - endpoints and injected environment names;
   - persistence volumes and lifetimes;
   - structured logs/traces;
   - actual Flutter process state.
7. Do not treat Aspire “Healthy” as proof of application connectivity.

### 1. Build the actual architecture map

Map these areas from source with file-and-line evidence:

- Kernel Abstractions, Core, Client, Kernel HTTP host, AppHost, MCP and Scripting.
- Every module’s Contracts, implementation and Aspire.Hosting projects.
- Flutter core, kit and shell.
- Module discovery/composition and all duplicate catalogs.
- Neuron activation, durable state, journals, outbox and delivery.
- Identity, principal, workspace membership and authorization.
- Chat/Conversation, AI and Execution ownership.
- OAuth, Integration/MCP, token state and webhook ingress.
- Surface state, sharing and Flutter projections.
- Direct infrastructure access such as Azure Tables, queues, blobs or Qdrant.

Produce a dependency graph showing which projects reference which. Flag every dependency that points against the intended module direction.

### 2. Create a state-ownership matrix

For every durable or security-sensitive state model, record:

- Domain concept.
- Canonical owner today.
- Physical persistence adapter.
- All readers and writers.
- Identity/key format.
- Duplicated representations.
- Invariants and where they are enforced.
- Cross-neuron/store transaction windows.
- Recovery/idempotency behavior.
- Intended canonical owner.
- Verdict: keep, deepen, move, merge, replace or delete after migration.

Pay particular attention to:

- `DigitalBrainUser.Id` versus `PrincipalId`.
- Username stored in account rows, claims, actor stamps and Workspace membership.
- `IsBootstrapOwner` versus current Workspace role.
- The `meta/bootstrap-owner` marker.
- Account creation followed by a separate Workspace mutation.
- User-created chat/surface name scoping.
- MCP’s fixed operator actor and bare chat names.
- OAuth state/token principal ownership.
- Execution versus Chat/Conversation responsibilities.
- AppHost and Kernel module catalogs.

### 3. Trace complete runtime journeys

Trace these end to end, including failure and recovery paths:

1. Clean local Aspire launch → development identity → Flutter authentication → four SSE/topology requests → connected status.
2. Production first-run bootstrap → login → cookie → `ActorContext`.
3. Owner creates another user → membership → private chat/integrations.
4. Flutter sends a message → durable conversation/Chat → Execution → AI → journal → SSE resume.
5. SSE disconnect/reconnect and `afterSequence` handling.
6. Northbound MCP authentication → principal resolution → same private conversation used by Flutter.
7. OAuth initiation → user-bound state → callback → token storage → resumed operation.
8. Scripting client connects to the cluster → generates/runs a single-file C# application.
9. Personal surface creation → explicit publication to Workspace.

For each journey, state where identity, authority, durability and retry decisions are made.

### 4. Review module quality

Use the vocabulary: module, interface, implementation, seam, adapter, depth, leverage and locality.

For each module ask:

- What is its single responsibility?
- What must callers know?
- Is its interface smaller than the complexity it hides?
- Is it a meaningful adapter or a pass-through middleman?
- Does deleting it remove complexity or scatter complexity into callers?
- Does it own invariants or merely shuttle DTOs?
- Does one logical change cause shotgun surgery?
- Are infrastructure details leaking through the seam?
- Are domain names honest and consistent?
- Is behavior duplicated across Host, MCP, Core, modules or Flutter?
- Does a class contain multiple reasons to change?
- Are there god switches, demo branches or fixture behavior in production paths?

Do not report style trivia. Report architectural consequences.

### 5. Examine identity as a first-class case study

Compare at least these designs:

A. Current ASP.NET Identity + direct `identityusers` Azure Table + Workspace neuron.

B. ASP.NET Core Identity/cookie handling at the Host with an internal grain-backed account store.

C. A workspace-scoped Identity neuron exposing a small internal interface for registration, authentication material and principal lookup, while WorkspaceNeuron remains authoritative for membership/roles.

You may propose a better fourth option if evidence justifies it.

For every option explain:

- Canonical identity and key.
- Username uniqueness.
- Password hashing and verification location.
- How password hashes are persisted without exposing raw passwords.
- Why credential operations are not journaled synapses.
- Bootstrap atomicity.
- Account/membership provisioning and recovery.
- Cookie invalidation/security stamp behavior.
- Local Aspire automatic development identity.
- Production bootstrap/login.
- Multi-silo behavior.
- Migration from an existing `identityusers` table.
- How MCP and Flutter resolve the same principal.
- What still physically uses Azurite locally.

Do not assume “make it a grain” automatically solves indexing, security or cross-grain atomicity. Demonstrate the complete design.

## Findings format

Assign stable identifiers:

- P0: security, corruption, data-loss or product unusable.
- P1: duplicated authority, broken invariants or unrecoverable workflows.
- P2: major coupling, shallow modules or expensive change amplification.
- P3: localized cleanup and documentation drift.

Every finding must contain:

- ID and severity.
- Short title.
- Exact source evidence with file and line.
- Current behavior.
- Concrete failure mode.
- Violated product invariant.
- Recommended seam and migration direction.
- Confidence level.
- Any owner decision required.

Do not call something “trash” without this evidence.

## Required deliverables

Create only review documents during Stage A; do not edit production source or existing architecture documents yet:

1. `plans/architecture-review/ARCHITECTURE-AUDIT.md`
   - executive summary;
   - actual project/runtime graph;
   - state-ownership matrix;
   - findings ordered by severity;
   - runtime journey traces.

2. `plans/architecture-review/TARGET-OPTIONS.md`
   - two or three coherent target architectures;
   - dependency direction;
   - canonical domain vocabulary;
   - identity design comparison;
   - trade-offs;
   - your clearly argued recommendation.

3. `plans/architecture-review/REFACTOR-ROADMAP.md`
   - ordered strangler slices;
   - prerequisites;
   - exact seam affected;
   - files/projects likely involved;
   - state/data migration;
   - compatibility constraints;
   - verification method;
   - rollback/recovery;
   - definition of done.

4. `plans/architecture-review/DECISIONS-REQUIRED.md`
   - only genuine owner decisions;
   - 2–3 concrete alternatives for each;
   - recommendation and consequences;
   - no questions whose answer can be discovered from source.

The roadmap must leave the product runnable after every slice. Reject a big-bang rewrite.

## Stage-A stopping gate

After writing and self-reviewing those documents:

1. Check for unsupported claims, missing evidence, contradictory recommendations and vague “TBD” entries.
2. Present Vlad with:
   - the five worst architectural findings;
   - the recommended target;
   - the proposed first refactoring slice;
   - the exact decisions requiring approval.
3. STOP.

Do not edit production code.
Do not delete files.
Do not build.
Do not commit.
Wait for explicit approval.

## Stage B — only after Vlad approves the target and first slice

Implement one approved seam at a time:

1. Characterize current source and live behavior.
2. Define the invariant and migration.
3. Make the smallest coherent change.
4. Adversarially review your own diff.
5. Stop AppHost before building.
6. Run:
   - `dotnet build DigitalBrain.slnx -warnaserror --nologo`
   - `flutter analyze lib` separately in Flutter core, kit and shell when affected.
   - Do not invoke the deleted `scripts/gate.ps1`; report that stale documentation referred to it.
   - Never run automated tests.
7. Start Aspire only after the build.
8. Verify through the real runtime:
   - Kernel health;
   - actual Windows Flutter status says connected;
   - send a real message through Flutter;
   - observe durable completion and SSE projection;
   - verify MCP independently, never as a substitute for Flutter;
   - inspect errors, structured logs and traces.
9. Update the architecture-review evidence.
10. Commit one semantic slice only on the branch Vlad authorizes.
11. Do not push or merge.

If a rule conflicts with current reality, stop that slice and record the conflict instead of inventing an exception.
