# Continuation Prompt: Core + Kernel Segregation, Packability & Self-Update (Elon's 5 Steps Applied)

**Date:** 2026-06-25 (continuation after previous session)
**Workspace:** E:\digitalbraintech\brain
**Follow @core-requirements + this exact prompt**

## The 5 Steps (Elon's "Algorithm") — Apply IN STRICT ORDER for every single decision or change

1. **Make your requirements less dumb**
   - Question every requirement. Trace it to a specific person or real need.
   - Challenge assumptions. "Kernel-dashboard in Core" is dumb if Core must be pure abstractions.

2. **Delete the part or process** (try very hard to remove as much as possible)
   - Ruthlessly delete. If you're not deleting ~10% of the time, delete more.
   - Remove "just in case" code, mixed concerns, primitive obsession, unnecessary strings.

3. **Simplify or optimize** (what remains)
   - Only after 1+2. Don't optimize garbage.

4. **Accelerate cycle time**
   - Speed up the right thing.

5. **Automate**
   - Last. Never automate a bad process.

**Key rule:** You must explicitly walk through steps 1-5 (in order) in your thinking before proposing or making any change. If you skip, the work is invalid.

## Core Vision & Requirements (from @core-requirements + vision)

- **DigitalBrain.Core** = pure class library with **core abstractions only**.
  - INeuron, Synapse (full causal lineage: SynapseId, CorrelationId, CausationId, Stamp), IPackBehavior (with manifest of handled synapse types), IHandle<T>, Checkpoint, INeuronStateProtector, basic UI contracts (UiSurface, RfwCard) that are universal.
  - **Zero implementation details.**
  - **Zero kernel-specific code** (no "kernel-dashboard", no kernel status surfaces, no kernel orchestrators, no silo-specific things).
  - No primitive obsession: no magic strings for kinds/ids, use proper types/records/enums where possible.

- **Kernel (DigitalBrain.Silo + supporting)** = the runtime.
  - The kernel **itself is a packable, marketplace-distributable thing**.
  - It can be published as a "kernel" pack and self-updated (like any other experience/pack) via the marketplace + orchestrator + rolling updates on replicas.
  - Kernel has **its own tests** (separate from Core tests).
  - Kernel-specific concerns live in kernel projects/packages:
    - KernelDashboard, kernel status surfaces, kernel rolling logic, specific orchestrators, etc.
    - Proper packaging so the whole kernel (or major parts) can be updated without full redeploy.

- **Segregation is non-negotiable**:
  - Core = universal, stable, minimal.
  - Kernel = runtime + self-updatable + HA (3 replicas) + its own surfaces/status.
  - Example violation: kernel-dashboard must **not** live in DigitalBrain.Core\UiSurfaces.cs. It belongs in kernel code.

- **Self-update model**:
  - Pre-installed "kernel" pack in marketplace.
  - Update = publish new version → install → embody (or trigger rolling restart) → rejoin with state preserved via checkpoints/journals.
  - Use explicit rolling: update 1 replica, drain, verify, next.

- **Best practices**:
  - Kill primitive obsession everywhere (string kinds → proper constants/types in the right project, string IDs → typed NeuronId/SynapseId where possible).
  - DDD principles, clean boundaries, design system consistency.
  - Kernel behaviors can migrate to updatable packs over time.
  - Excellent test coverage with Reqnroll for distribution/self-update/UI surfaces + xunit.

- **Testing segregation**:
  - Core has its own focused tests.
  - Kernel has its own focused tests (rolling, self-update, HA, kernel surfaces, etc.).

## Current State (after previous work — you must re-research)

- Core has PackManifest + GetManifest on IPackBehavior (good start for typed dispatch).
- Causal query APIs added (GetCausalLineageAsync etc.).
- Some rolling support and kernel-dashboard surface exist (but **mixed into Core** — violation).
- Kernel update publishes a "kernel" pack with payload.
- MarketplaceSeeds now has a kernel entry (partial).
- Versions bumped, some test expansion.
- Still problems:
  - Kernel-specific UI (dashboard, rolling surfaces) leaking into Core.
  - Primitive obsession remains (string "kernel-dashboard", magic strings).
  - Kernel not yet fully modeled as a first-class packable/runtime with clear boundaries and its own packaging/tests.
  - Self-update/rolling is partial (needs explicit drain/verify/rejoin proofs + segregation).
  - Tests and packaging need strengthening for the vision.

You **must** start by:
- Running `aspire doctor` (MCP)
- High-severity tests focused on core + kernel + UI + rolling/self-update
- Deep research reads/greps on current segregation issues (especially UiSurfaces.cs, kernel-dashboard, string kinds, MarketplaceSeeds, orchestrators, etc.)

## Tasks for This Session (apply 5 Steps to each area in order)

1. **Research & Make Requirements Less Dumb**
   - Question the current mixing. Why is anything kernel-specific in Core?
   - Trace: Core should be abstractions only. Kernel is the thing being updated.
   - Identify all primitive obsession and boundary violations.

2. **Delete (aggressively)**
   - Move/delete kernel-specific code from DigitalBrain.Core (UiSurfaces, dashboard, rolling surfaces, etc.) into proper kernel location (e.g. DigitalBrain.Silo.Ui or a dedicated kernel surface package that can itself be updated).
   - Remove duplicated or "just in case" code.
   - Clean stringly-typed "kinds" — introduce proper constants/types **in the owning project** (kernel owns its kinds).

3. **Simplify / Optimize what remains**
   - After deletion: simplify the remaining abstractions and kernel runtime.
   - Make kernel packaging clean so "kernel" pack can carry update payload/scripts.

4. **Accelerate cycle time**
   - Fast inner loops for core vs kernel changes (separate test projects help here).

5. **Automate last** (only if it makes sense after cleanup).

**Concrete goals (only after applying 5 steps):**
- Pure Core with only universal abstractions. Kernel-dashboard etc. fully moved out.
- Kernel modeled as packable/self-updatable (publish "kernel" pack → install → rolling update with checkpoints).
- Kernel has its own tests and (where appropriate) its own packaging.
- Kill more primitive obsession (kinds, ids, etc.).
- Expand excellent Reqnroll coverage for self-update/rolling/UI surfaces from the segregated kernel.
- Update MarketplaceSeeds + update paths to treat kernel as proper versioned pack.
- Document segregation and decisions in the continuation md (or new md).
- Full verification: build + high-sev tests (core separate, kernel separate) + aspire doctor + mcp.

## Non-negotiable Rules for this session (and future)

- Apply Elon's 5 steps **explicitly in thinking** before any edit or proposal.
- Use Context7 for **every** API/framework detail before writing code.
- Relative paths only. Never touch C:\Users\ paths.
- After **every** change: `dotnet build`, high-severity tests (separate filters for core vs kernel if possible), `aspire doctor` (MCP), relevant aspire mcp tools.
- Latest versions of nuget packages (via central props).
- No default `/// <summary>` garbage. Only meaningful comments.
- Self-explanatory names (no "handler", use PackEmbodimentDispatcher or similar where appropriate).
- Research first with reads + greps on brain/DigitalBrain.{Core,Silo}/ and docs.
- Output working code + green tests + updated continuation docs.

**Start by:**
1. `aspire doctor` (MCP)
2. High-severity tests (core + kernel + UI + self-update)
3. Research reads/greps on current mixing and primitive obsession
4. Then apply 5 steps and implement.

**Success criteria:**
- Core is clean abstractions only.
- Kernel-specific code (including dashboard) lives in kernel.
- Kernel is demonstrably packable/self-updatable via marketplace.
- Primitive obsession reduced.
- Excellent segregated tests.
- All verifs green.
- Clear documentation of the new boundaries.

Paste this entire prompt (plus the original continuation-core-kernel-best-impl.md if needed) as the next user message when ready to continue the work.
