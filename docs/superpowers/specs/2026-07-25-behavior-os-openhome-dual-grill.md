# Behavior OS residual grill — OpenHome / PostAuthBootstrap dual

Wave: **B0** · Agent: **14** · Mission: `delete-trash` / `own-audit`  
Write scope: **this file only** (no product code deletes this cycle)  
HEAD at grill: `7ffaa21a415ed676ea4735cab06fa2de29a2b4d4`  
`git status --porcelain` (session start): untracked B0 specs + Explicit BDD residuals only

Vision restatement: **Framework = neurons + synapses; OS = behaviors (including UI); compositions preview Behavior identity before the install rail.**

Primary question:

> Should we **delete** `PostAuthBootstrap` as dual of `OpenHome`, **fold** it into `BootOnActivation`, or **keep both names** for semantic roles (post-auth orchestration vs pure open home)? Hard recommend for **B3** migration. Prefer residual hold this cycle.

Related residuals (not owned here):

| Residual | Owner |
| --- | --- |
| Who emits `DigitalBrainActivated` | emitter grill (agent 4) |
| Activation synapse package home | activation-synapse / package-graph grills |
| Flutter first screen = home not login | flutter-reaction grill |
| `BootOnActivation` body + red→green | B3 `behavior-impl` |

---

## 1. Codegraph paste (mandatory)

### Query

```
OpenHome PostAuthBootstrap NavigateShell composition dual BootOnActivation
```

### Blast radius (codegraph)

| Symbol | Callers / dependents |
| --- | --- |
| `OpenHome` | 5 callers — `ShellAndSurfaceCompositions`, `BehaviorOsActivationHonesty`, `BehaviorOsActivationBoot` (constants); Navigate uses `SceneKey`/`SceneTitle` only |
| `PostAuthBootstrap` | **2 callers only** — same L1 test file + Explicit dual honesty residual |
| `IShell.Open` / `ShellNeuron.Open` | Shared substrate — compositions, surfaces, Ui edge mutator |
| `BootOnActivation` | **No type in tree** — string residual in Explicit BDD (`BehaviorOsActivationBoot`) only |
| Production AppHost / silo / Flutter host | **Zero** references to either composition type |

### Bodies (verbatim dual)

| Type | Body today | Constants |
| --- | --- | --- |
| `OpenHome` | `brain.Get<IShell>(shell).Open(OpenScene(…, SceneKey, SceneTitle))` | **Owns** `"home"` / `"Home"` |
| `PostAuthBootstrap` | same `IShell.Open` with **literal** `"home"` / `"Home"` | **Does not** reference `OpenHome.SceneKey` |
| `NavigateShell` | multi-scene open helper | Parameterized; not dual |
| `BootOnActivation` | **unbuilt** | Designed: react to activation → compose `OpenHome` |

**Outcome dual:** both journal the same `SceneOpened` (home/Home). Explicit residual already pins this:

```text
RESIDUAL dual product sentence: PostAuthBootstrap and OpenHome both open home today
```

**Consumer dual:** both are **test pull-invoked only** (architecture §5). No unique production consumer for either type. `PostAuthBootstrap` has **no unique journal outcome** relative to `OpenHome`.

**Not dual:** product *sentences* and future triggers (architecture + campaign design):

| Name | Product sentence (intended) | Trigger (Designed) |
| --- | --- | --- |
| `OpenHome` | Pure open-home primitive — scene key/title constants + single open | Pull-invoke / composed by others |
| `PostAuthBootstrap` | Post-auth UX orchestration after edge binds principal — **not** IdP, **not** authentication | Post-auth / post-bind (edge principal → OwnerId) |
| `BootOnActivation` | OS reacts to activation synapse → first screen | `DigitalBrainActivated` committed |

---

## 2. Options grill

### Option A — Delete `PostAuthBootstrap` as dual of `OpenHome`

| | |
| --- | --- |
| **Claim** | Bodies are identical; delete the duplicate class and one L1 test; keep `OpenHome` only. |
| **Evidence for** | Byte-level open-home dual; 2 test callers; zero product callers; literals ignore `OpenHome` constants (drift risk). |
| **Evidence against** | Architecture §5 / §4.6 names **post-auth composition** as a distinct honesty phrase; design R4 keeps both names; packages.md lists both; folding *names* erases the post-auth vs pure-open split before IdP edge needs orchestration steps beyond open-home. |
| **Delete-now bar** (mission): trivial dual + zero unique consumers | Bodies dual **yes**; unique *code* consumers **no**; unique *semantic / doc / residual* role **yes**. Bar for delete **fails** — residual hold. |
| **Verdict B0** | **Hold** — do not delete this cycle. |
| **Verdict B3** | **Reject hard delete** unless post-auth orchestration is proven never needed *and* activation boot fully owns first screen under a single Behavior identity. |

### Option B — Fold `PostAuthBootstrap` into `BootOnActivation`

| | |
| --- | --- |
| **Claim** | One boot composition reacts to activation and replaces post-auth bootstrap name. |
| **Evidence for** | Today both “get owner into home”; fewer sealed classes; Explicit dual residual disappears if one identity owns first screen. |
| **Evidence against** | **Different triggers.** Activation fact ≠ edge auth completion. Collapsing names teaches “auth is OS boot” and fights §4.6 Auth edge (credentials/IdP stay edge). Emitter grill already forbids collapsing emit+open into one god composition long-term. design.md R4: keep both names. |
| **Verdict B0** | **Hold** — `BootOnActivation` not even a type yet. |
| **Verdict B3** | **Reject hard fold of name.** Introduce `BootOnActivation` as **third** sealed class composing `OpenHome`; do not rename `PostAuthBootstrap` into it. |

### Option C — Keep both names for semantic roles (post-auth vs pure open home)

| | |
| --- | --- |
| **Claim** | Names are future Behavior identities; body dual is residual trash; constants ownership lives on `OpenHome`. |
| **Evidence for** | architecture §5 shell-only split; design §5–8 + R4; packages.md; flutter-reaction grill; emitter residual table; CompositionBehaviorShape (one public sealed class = identity). |
| **Evidence against** | Two classes that do the same thing today train authors that dual product sentences are fine; honesty residual exists for a reason. |
| **Mitigation (B3, not B0 delete)** | Kill **body** dual: `PostAuthBootstrap` → compose `OpenHome` (literals die); `BootOnActivation` → compose `OpenHome`; only one place owns scene constants and `IShell.Open` for home. Keep residual Explicit until product BDD shows distinct non-open steps or deliberately retires a name. |
| **Verdict B0** | **Accept names; residual hold on delete.** |
| **Verdict B3** | **Hard recommend Option C + body compose; no name collapse.** |

---

## 3. Hard recommendation (B3 migration)

```
Recommendation (B3 — HARD):
  KEEP OpenHome as the sole open-home primitive (constants + IShell.Open body).
  KEEP PostAuthBootstrap as a distinct name for post-auth UX orchestration
    (not auth, not IdP, not activation emitter).
  ADD BootOnActivation as a third sealed composition that REACTS to
    DigitalBrainActivated and COMPOSES OpenHome — do not fold PostAuthBootstrap
    into BootOnActivation and do not delete PostAuthBootstrap as dual trash.
  At B3 body migrate only: PostAuthBootstrap.RunAsync must call OpenHome
    (delete literal "home"/"Home"); BootOnActivation composes OpenHome the same way.
  One open-home implementation path; three product sentences / Behavior identities.

B0 this cycle:
  RESIDUAL HOLD — no product code delete. Dual is proven on body + journal outcome
  but NOT on product sentence. Unique consumers are tests + architecture naming;
  mission prefers hold unless trivial dual with zero unique role.

Strongest argument against (delete / fold now):
  Two sealed classes that journal the same SceneOpened are pure waste; B3 will only
  invent a third (BootOnActivation) and make three duals; delete PostAuthBootstrap
  today and let BootOnActivation + OpenHome cover first screen.

Defense / fold:
  Fold the *bodies*, not the *names*. Architecture already separates (1) pure open,
  (2) post-auth orchestration, (3) activation reaction. Deleting the post-auth name
  before multi-principal IdP edge exists collapses a designed phrase into open-home
  theater. Folding post-auth into BootOnActivation conflates edge bind with
  DigitalBrainActivated. The trash is the literal dual in PostAuthBootstrap and the
  missing BootOnActivation type — not the OpenHome identity.

Evidence:
  codegraph: OpenHome 5 callers / PostAuthBootstrap 2 callers — all tests;
  samples/.../OpenHome.cs owns SceneKey/SceneTitle;
  samples/.../PostAuthBootstrap.cs opens literals "home"/"Home";
  BootOnActivation absent as type (Explicit strings only);
  architecture.md §5 shell-only + post-auth composition phrase;
  design.md R4 keep both names; §5 compose OpenHome from BootOnActivation;
  BehaviorOsActivationHonesty Explicit dual residual;
  CompositionBehaviorShape: peer types forbidden on surface — body compose OpenHome OK.
```

### B3 migration checklist (pointer only — not this file’s implement scope)

| Step | Action | Delete? |
| --- | --- | --- |
| 1 | Red→green activation → `BootOnActivation` → `SceneOpened` home | no |
| 2 | Add `BootOnActivation` sealed class; body `await new OpenHome().RunAsync(...)` | no |
| 3 | Change `PostAuthBootstrap` body to compose `OpenHome` (literals → constants path) | **body dual only** |
| 4 | Keep L1: `OpenHome` journals home; optionally collapse pure-duplicate `PostAuthBootstrapOpensHome` into compose-shape / honesty assert | test only if green |
| 5 | Retire Explicit dual residual when body dual is gone **or** promote to permanent “two triggers may both open home” honesty | residual |
| 6 | **Later** delete `PostAuthBootstrap` name only if product sentence dies (no post-auth orchestration consumer ever) | delete-grill again |

**Must not B3:** host `Program` open-home; `IBehavior` / name dispatch; login-as-Behavior-auth; second home constant set; fold emit+open into one god type.

---

## 4. Recommendation form (grill)

### R1 — Keep `OpenHome` (primitive)

| Field | Content |
| --- | --- |
| **Recommendation** | `OpenHome` remains sole owner of home `SceneKey`/`SceneTitle` and the single `IShell.Open` home body. |
| **Strongest argument against** | Inlined open is three lines; constants could live on a static bag without a composition type. |
| **Defense or fold** | **Defend:** future Behavior identity + composition shape pins require a sealed class; Navigate/surfaces already consume constants; BootOnActivation must compose a named primitive. |
| **Evidence** | `OpenHome.cs`; L1 `OpenHomeCompositionJournalsSceneOpened`; Navigate uses constants; design §5. |
| **Consumer today** | Composition tests + residual BDD strings + docs. |

### R2 — Keep `PostAuthBootstrap` name (do not delete B0/B3 hard)

| Field | Content |
| --- | --- |
| **Recommendation** | **Keep name** for post-auth UX orchestration role; **do not delete** this cycle; B3 fix body dual only. |
| **Strongest argument against** | Body is pure dual; zero unique product consumer → delete is the loop’s “delete” step. |
| **Defense or fold** | **Defend names / fold bodies:** dual product *outcome* is residual trash; dual product *sentence* is not yet falsified. Mission: residual hold unless trivial dual with zero unique role — semantic role still load-bearing in architecture §5 / §4.6. |
| **Evidence** | architecture shell-only list; packages.md; design R4; Explicit dual residual exists *because* outcome dual is known and name dual is deliberate hold. |

### R3 — Do not fold into `BootOnActivation`

| Field | Content |
| --- | --- |
| **Recommendation** | `BootOnActivation` is a **new** activation reactor composing `OpenHome`. Never rename/fold `PostAuthBootstrap` into it. |
| **Strongest argument against** | First screen after “ready” is one UX moment; one Behavior should own it. |
| **Defense or fold** | **Defend:** activation synapse and post-auth bind are different facts/triggers. One UX moment can share `OpenHome` without sharing Behavior identity. Collapsing names re-opens login/auth-in-OS confusion. |
| **Evidence** | design §5 reaction chain; emitter grill (separate reactor); flutter-reaction (home not login); Auth edge §4.6. |

### R4 — B0 product delete = no

| Field | Content |
| --- | --- |
| **Recommendation** | **No product code deletes this cycle.** Optional later: only the literal dual (B3) and docs that claim two independent open-home *implementations*. |
| **Strongest argument against** | “Delete trash” mission wants green delete now. |
| **Defense or fold** | **Fold mission to residual hold:** trash is body dual + missing third identity, not the post-auth name. Deleting now would thrash architecture/packages/design residuals mid-campaign. |
| **Evidence** | mission text; zero production callers but non-zero architecture naming consumers. |

---

## 5. Thirteen grill answers

1. **What is the product sentence?**  
   After an owner is ready (activation and/or post-auth bind), OS logic opens shell home via Flutter vocabulary so `SceneOpened` journals first screen — pure open, post-auth orchestration, and activation reaction are **related sentences**, not one class.

2. **Is it framework vocabulary, module neuron, OS behavior, edge, or test witness?**  
   All three names = **OS behavior** (pre-rail compositions). `IShell`/`SceneOpened` = module vocabulary. Proofs = test witness. Edge does not own boot policy.

3. **Belongs in proposed home? If not: delete / move / internalize / re-home as behavior?**  
   Stay in `samples/DigitalBrain.Compositions` / `DigitalBrain.Shell`. **Do not delete** `PostAuthBootstrap` name. **Do not move** to Kernel/Ui/Flutter.Contracts. B3 re-homes logic shape as Behaviors only when install rail exists — same class identities.

4. **Aligns with framework = neurons+synapses, OS = behaviors?**  
   Yes if open is `IShell.Open` → neuron emits synapse; compositions/Behaviors own *when*. No if host Program or edge invents open-home policy without journal truth.

5. **Consumer today?**  
   Tests only (`ShellAndSurfaceCompositions`, Explicit honesty/boot). Architecture/docs name both. **No** production silo/AppHost consumer. Campaign (Behavior OS) is the forward consumer of the *split*, not of the body dual.

6. **Built vs Designed honesty?**  
   `OpenHome` / `PostAuthBootstrap` bodies = **Built** samples (pull-invoked). Outcome dual = **Built residual**. `BootOnActivation` + activation chain = **Designed**. Install rail = **Designed unbuilt**. Do not claim Behaviors installed.

7. **New public product API?**  
   **None** from this grill. B3 adds sample sealed class `BootOnActivation` only (not NuGet product API).

8. **Package graph blast?**  
   None for hold. Body-compose `OpenHome` stays inside compositions assembly (CompositionBehaviorShape allows body `new OpenHome()`; forbids peer types on ctor/method **surface**).

9. **Dual path risk?**  
   **Yes — present.** Two pull-invoked classes → same `SceneOpened`. Mitigate by single implementation (`OpenHome`) composed by others; do not add a fourth open-home. Host/Ui POST open-scene is a separate dual mutator (flutter-reaction grill) converging on the same fact — keep, do not claim as activation product path.

10. **Must-not-return?**  
    `IBehavior*` / name dispatch; Kernel domain login; passwords in journals; host special-case open as product truth; deleting architecture “post-auth composition” phrase without consumer proof; inventing login scene as first Behavior auth.

11. **Delete / simplify opportunities?**  
    **B0:** none in product code (hold).  
    **B3:** delete **literals** in `PostAuthBootstrap`; optionally collapse purely redundant L1 if compose-shape covers it; **do not** delete `PostAuthBootstrap` type without a second grill when post-auth steps exist or are rejected.  
    **Later:** if IdP edge never needs post-auth OS orchestration beyond activation boot, re-grill delete of the name.

12. **Test oracle for the claim?**  
    L1: `OpenHomeCompositionJournalsSceneOpened`, `PostAuthBootstrapOpensHome` (today). Explicit: dual residual + activation boot residuals. B3 green: activation commit → `BootOnActivation` → home `SceneOpened` with `OpenHome` constants. Root gate remains full solution test when implement ships — this file is design-only.

13. **What did we not invent?**  
    No types, no deletes under `samples/` or `src/`, no package moves, no `IBehavior`, no host wiring, no login scene. Only this dual grill + hard B3 name/body rule.

---

## 6. Decision log

| Decision | Choice | Strongest counter | Defense / fold |
| --- | --- | --- | --- |
| Delete `PostAuthBootstrap` now? | **No (residual hold)** | Body dual + test-only callers | Semantic role still in architecture; mission prefers hold |
| Fold into `BootOnActivation`? | **No** | One first-screen Behavior | Different triggers; third type composes primitive |
| Keep both names? | **Yes (hard B3)** | Trains dual sentences | Sentences differ; bodies must not |
| Who owns home constants? | **`OpenHome` only** | Scatter on each composer | Single source; B3 compose |
| B0 product delete? | **None** | Mission delete-trash | Trash = body dual, fixed at B3 not by name delete |

---

## 7. Wave handoff

| Order | Work | Mission | Depends |
| --- | --- | --- | --- |
| B0 (this) | Dual grill freeze: keep names; hold delete | `delete-trash` / own-audit | Done in this file |
| B1–B2 | Activation synapse + emitter | synapse-vocab / emitter | Other B0 grills |
| B3 | `BootOnActivation` + compose `OpenHome`; body-fix `PostAuthBootstrap` | `behavior-impl` | Red BDD activation→home |
| B3+ | Re-grill delete of `PostAuthBootstrap` only if post-auth sentence dies | delete-trash | Real IdP/post-auth consumer proof or rejection |

**Non-binding score for campaign scorecard:** dual **outcome** residual = open; dual **name** intentional = keep; B3 body migrate = required; B0 delete = **rejected**.
