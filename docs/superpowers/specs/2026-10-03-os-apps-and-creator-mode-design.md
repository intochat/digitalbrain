# OS apps, runtime apps, and Creator mode

Status: ratified direction, not yet implemented. Companion to
`2026-09-29-programmable-brain-vision-design.md`; this document settles where the operating
system ends, where apps begin, and how the live app-development experience works for both OS
developers and users.

## The problem this settles

Three confusions kept resurfacing:

- **IntoChat plays three roles at once**: OS distribution (composes all modules, ships Settings,
  owns the shipping pipeline), app-store base image (`ShippedAppPublisher`), and third-party app
  vendor (CustomerResearcher).
- **The OS has no project.** The OS composition exists three times by duplication: the IntoChat
  `Program.cs` module list, `ReferenceBrain.Create()` in Testing.E2E, and the
  `referenceComposition` branch of `ModuleHostProgram`. "The reference composition" *is* the
  operating system; it never got a name, so it lives as a test artifact and IntoChat absorbed its
  responsibilities by default.
- **Assistant is both a module and a shipped app.** `DigitalBrain.Modules.Assistant` carries the
  `/agent` stream, transcription, receipts, turn execution and presentation; `Apps/Assistant` is
  a shipped package with a spec. Nothing says which half belongs where.

## The defining principle

**Everything above the kernel is an app in package form. The only axis is when it compiles.**

|  | OS apps (Assistant, Settings, Authoring) | Runtime apps (CustomerResearcher, user apps) |
|---|---|---|
| Form | `app.spec.md` + `tests.cs` + `behaviors/*.cs` | identical |
| Verification | the same commit → verify → publish gate | identical |
| Compilation | build time: behaviors compile into an OS assembly and execute in-silo as OS-level neurons | runtime: `dotnet run` in the sandbox over the script edge |
| Presence | always installed, pinned revision, cannot be uninstalled | installed per brain |

Modules shrink to what the module contract always said: **connectors and kernel capabilities** —
Gmail's edge, the Flutter shell as the human's workspace connector, and the conversational edge
(`/agent` stream, voice transcription, compute metering/receipts) as the human's conversational
connector. The Assistant *persona* — which model, what prompt, what it does with signals — is the
first OS app.

The ladder, top to bottom:

```
runtime apps     packages, sandbox-compiled, installed per brain   (CustomerResearcher, user apps)
OS apps          packages, build-compiled, always present          (Assistant, Settings, Authoring)
modules          C# connectors + kernel capabilities               (Gmail, Flutter, CSharp, ...)
kernel/platform  the privileged rings
```

A capability is a **module** iff it needs an edge to an external system or a privileged
capability scripts cannot express. Everything expressible through contracts belongs in an app.

## Project shape

```
DigitalBrain.OS            the named composition: kernel + platform + module catalog
                           + the OS apps, shipped as first-party packages
DigitalBrain.OS.Tests.E2E  today's ReferenceBrainFixture, legitimized: composition health,
                           trace budgets, the self-programming golden journey
IntoChat                   a distribution/brand on the OS: picks modules, brands the shell,
                           ships ITS apps (CustomerResearcher = the in-tree third-party proof)
IntoChat.Tests.E2E         brand golden journeys only: a real dev shipped a real app
                           on the runtime
```

The three duplicated module lists collapse into one `OS.Compose()` that IntoChat's `Program`,
the OS E2E fixture, and the module host all call. A distribution's own composition is a *diff*
against the OS: add/remove modules, brand options. IntoChat's third-party role stays in-tree
deliberately — it is the permanent proof that the public surface suffices, enforced by the
existing "shipped apps use only shipped contracts" checks.

## Creator mode: the pipeline run on every save

The live development experience is not a new subsystem; it is the existing package pipeline
driven by a connector, observable because every step is signals.

- **The mounted folder is a connector.** External system = the filesystem/IDE. The sandbox
  already mounts the repo (`CSharpOptions.SourceRoot`); a dev-mode package source is the same
  move: `IPackage` gains a *working-tree* mode whose source of truth is a mounted folder instead
  of committed revisions. A file watcher publishes a `SourceChanged` signal, provenance stamped
  like any edge.
- **Save → draft install.** On `SourceChanged`, a builder behavior commits a *draft revision* and
  reinstalls it into the creator's own brain, skipping the publish gate. The gate protects other
  people; your own brain is your blast radius (the distinction scratch installs already rely on).
- **UI appears by itself.** A window is a neuron; the shell re-derives the workspace wholesale
  (derived state is re-derived, never patched). When the draft install activates and its behavior
  creates UI neurons, the next derivation shows the window. No new mechanism.
- **Scenario ↔ window association** lives where scenario↔source association already lives:
  `app.authoring.json`. The Apps view shows, per scenario, the behaviors it binds and the live
  windows those behaviors created. Create scenario → Author writes spec → Builder emits behavior
  → draft install → window docks beside the scenario.
- **Hello world is the package template.** "New app" scaffolds one scenario ("When the app opens,
  a window shows 'Hello, world'"), one behavior, one test. The template is simultaneously user
  onboarding, the dev-mode smoke test, and Creator mode's own E2E golden journey.

**Dogfooding closes the loop.** OS apps are folders in the repo. In dev mode those folders mount
the same way, so editing Assistant's spec as an OS developer and editing CustomerResearcher as
"IntoChat the vendor" is the same experience. OS developers are Creator mode's first daily users;
that is the guarantee the live-UI mechanism works for end users.

## The three hard decisions (open, do not improvise)

1. **Draft installs vs the publish gate.** A draft revision is installable only in the authoring
   brain, never shareable or forkable, and visibly marked. Anything looser opens a hole in the
   gate.
2. **Precompiled runtime fidelity.** OS-app behaviors must not quietly gain powers runtime
   behaviors lack: in-silo code can touch what scripts cannot. Discipline: precompiled behaviors
   reference only contracts assemblies, held by an architecture test with the same shrink-only
   ratchet style as `TestTierFacts`.
3. **Watch loop vs at-least-once.** Rapid saves produce overlapping draft installs. The
   watermark/dedup model carries it, but "latest save wins, cancel the in-flight build" must be
   designed, not assumed.

## Sequencing

1. Finish the module test migrations (orthogonal; shrinks `TestTierBaseline.json`).
2. Extract `DigitalBrain.OS`: one composition, three call sites collapse;
   `DigitalBrain.OS.Tests.E2E` legitimizes `ReferenceBrainFixture`.
3. Move Settings/Assistant/Authoring package ownership to the OS; IntoChat keeps
   CustomerResearcher.
4. Precompiled package runtime; Assistant is its proof.
5. Creator mode: working-tree package source + draft installs + Apps-view live windows +
   hello-world template.

De-risk before building 4: a thin vertical slice of 5 on the *existing* sandbox runtime — mount
CustomerResearcher's folder, save a behavior, watch its window re-dock. If that loop feels right,
the rest of the design is validated; if not, the precompiled runtime was not the problem.
