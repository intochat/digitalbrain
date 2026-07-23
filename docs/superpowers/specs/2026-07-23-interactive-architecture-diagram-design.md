# Interactive architecture diagram

**Date:** 2026-07-23
**Status:** approved design, not yet implemented
**Prototype:** the three-tier design was iterated and approved as a self-contained HTML/JS prototype; this spec describes porting it to a VitePress Vue component.

Add one interactive component to the DigitalBrain docs that renders the architecture as three tiers — the platform, its actors, and the scripting layer — and lets a reader explore what each module ships and how a behavior composes the pre-built vocabulary.

---

## 1. Problem

`docs/architecture.md` is accurate prose, but a newcomer meets the whole system as a wall of text. The relationships that make DigitalBrain distinctive are spatial and hard to carry in prose: modules ship a *bundle* (neurons, synapses, MCP tools, Aspire hosting, UI) onto a kernel; people and agents act on that vocabulary; and a behavior is ordinary C# that *composes* existing vocabulary. A diagram that a reader can hover and click conveys those relationships in seconds and turns the page's own thesis — "a brain you program by composing its typed vocabulary" — into something visible.

The docs site currently has no custom theme and a test that asserts none exists (`docs/tests/site.test.mjs:47`). This is the first interactive component.

## 2. Goal

- One interactive diagram, embedded near the top of `docs/architecture.md`, expressing the three-tier model.
- Zero external dependencies — hand-authored inline SVG and Vue reactivity, matching the site's existing constraint (`vitepress@^1.6.4` only).
- Accurate to the repository: real module names, contracts, synapses, MCP/Aspire facets, and the AI model configuration.
- Theme-aware (light/dark via VitePress), responsive, and keyboard-accessible.
- The data is a single, editable source separated from the view.

## 3. Non-goals

- No graph library, diagram library, animation library, or Mermaid. The whole point is a self-contained component.
- No pan/zoom — the graph is small enough to fit on screen (this was evaluated and rejected during design).
- No generation of the diagram data from the registry yet. Data is hand-authored for this version; a future generator is out of scope.
- No second interactive page. One component, one embedding.
- No change to any framework code, `.cs`, `.csproj`, or the architecture prose itself beyond adding the component tag.

## 4. The approved design

Three vertically stacked tiers, a detail panel to the right, and status color-coding throughout (Built emerald, Designed amber, out-of-scope slate, kernel/accent cyan, synapse/fact violet).

**Tier 01 — DigitalBrain (the platform).** The largest tier. A kernel strip on top, then a grid of module cards. Each card shows two rows of vocabulary — **Neurons** (capability chips, e.g. `IGmail`, `ILLM`) and **Synapses** (fact chips with a `↯` mark, e.g. `AttemptSucceeded`, `ReminderElapsed`) — and an "also ships" footer with `MCP` / `Aspire` / `UI` pills for the other facets it contributes. Clicking a module opens its full breakdown in the panel: Neurons, Synapses, MCP tools, Aspire hosting (resources + parameters), UI, each shown or marked "— none". The **AI** module additionally shows its real model configuration (`Ollama · llama3.2`, `OpenAI · gpt-5.6`, the secret `openai-api-key`, and the `WithLlm<Llama32>().WithLlm<Gpt56>()` snippet).

**Tier 02 — People & Agents.** Two actor cards. People (built) operate through the owner-bound client and hold approval authority; Agents (designed) are LLM-powered neurons that act under the same approval rail.

**Tier 03 — Behaviors (scripting).** Behavior cards. **Selecting a behavior lights up, in tier 1, the exact neurons and synapses it composes** while everything else dims, and shows the behavior's single-file C# in the panel. This is the payoff interaction: it makes "a behavior composes existing typed vocabulary" literally visible. Two examples — *Morning digest* (composes `ReminderElapsed`, `IReminder`, `IGmail`, `ILLM`) and *Lead follow-up* (composes `ApprovalRequired`, `ISalesforce`, `ICountdown`).

Connectors between tiers are labeled with the real flow (*actors operate ↑↓*, *author ↓*) and highlight on the active path.

**Honesty markers.** Tier 1 (kernel, modules, neurons, built synapses, AI/Google/Salesforce hosting) is Built; agents, behaviors, and the approval rail are the ratified vision and badged Designed; behavior scripts are illustrative of the shape, not copied from a compiled file.

## 5. Component architecture

Three new files under a new `docs/.vitepress/theme/`, plus one edit each to the config, the test, and the page.

| File | Responsibility |
|---|---|
| `docs/.vitepress/theme/architecture-data.js` | The single source of truth for diagram content: the `MODULES`, `BEHAVIORS`, `ACTORS`, and `KERNEL` data. Plain data, no view logic. Editable without touching the component. |
| `docs/.vitepress/theme/ArchitectureMap.vue` | The view. Renders the three tiers and the detail panel, owns hover/click/pin state and the behavior-compose highlight. Imports the data file. Scoped styles. |
| `docs/.vitepress/theme/index.js` | Extends VitePress `DefaultTheme` and registers `ArchitectureMap` as a global component via `enhanceApp`. |
| `docs/.vitepress/config.mts` | No change required unless a nav entry is wanted; the component is used inline in a page. |
| `docs/tests/site.test.mjs` | Replace the `.vitepress/theme` must-not-exist assertion with a positive assertion (see §9). |
| `docs/architecture.md` | Add the `<ArchitectureMap />` tag once, immediately after §1 The vision. |

If `ArchitectureMap.vue` grows past a comfortable size, the detail panel is the natural unit to extract into a `DetailPanel.vue` child; the first version may keep it inline.

## 6. Data model

`architecture-data.js` exports four structures. Shapes:

```js
// one per module
{
  id: 'ai', label: 'AI',
  status: 'built' | 'designed' | 'scope',
  section: '#ai',                 // in-page anchor into architecture.md (see §8)
  role: 'one-sentence role',
  neurons: ['ILLM', 'IAgent', ...],       // capability contracts
  synapses: ['AttemptSucceeded', ...],    // typed facts ([] if none)
  mcp: false,                             // ships a private MCP catalog
  ui: false,                              // ships a UI surface
  aspire: [                               // Aspire resources ([] if none)
    { res: 'Ollama', sub: 'data volume', model: 'llama3.2', params: [] },
    { res: 'OpenAI', sub: '', model: 'gpt-5.6', params: ['openai-api-key'] }
  ],
  example: true                           // show the model-config snippet (AI only)
}

// one per behavior
{
  id: 'digest', label: 'Morning digest', status: 'designed',
  trigger: 'on ReminderElapsed',
  uses: ['ReminderElapsed', 'IReminder', 'IGmail', 'ILLM'],  // must match tier-1 chip text
  role: 'one-sentence description',
  script: '…C# source as a string…'
}

// KERNEL: { label, role, owns: ['Neuron','Synapse','CapabilityDelegation'] }
// ACTORS: people (built) and agents (designed), each { label, status, role }
```

The `uses` array on a behavior must contain exact chip strings that appear in tier 1, because the highlight matches on `data-tok` equality. This coupling is intentional and is guarded by a test (§9).

The seven modules and their real data (verified against the repository during design):

- **AI** (built): neurons `ILLM`, `IAgent`, `IGroupChat`, `ILlama32`, `IGpt56`; no synapses; no MCP; Aspire Ollama→`llama3.2` and OpenAI→`gpt-5.6` with secret `openai-api-key`; no UI; model-config example.
- **Tasks** (built): neurons `ITask`, `IWorker`; synapses `AttemptSucceeded`, `AttemptFailed`, `AttemptWaiting`, `ApprovalRequired`, `AttemptOutcomeUncertain`; no other facets.
- **Google** (built): neuron `IGmail`; MCP yes; Aspire Google OAuth with `google-client-id`, `google-client-secret`, `google-redirect-uri`.
- **Salesforce** (built): neuron `ISalesforce`; MCP yes; Aspire Salesforce OAuth with `salesforce-client-id`, `salesforce-client-secret`, `salesforce-redirect-uri`.
- **Time** (designed): neurons `ICountdown`, `IReminder`; synapses `CountdownElapsed`, `ReminderElapsed`, `ReminderOverdue`.
- **Flutter** (designed): neuron `IFlutter`; UI yes.
- **Memory** (scope): nothing.

## 7. Interactions

- **Hover** a module, behavior, actor, or the kernel → the panel shows its detail; the relevant tier connectors highlight. Hover does nothing while a node is pinned.
- **Click** any node → pin it (click again to unpin). Pinning survives mouse-out so a reader can study the panel.
- **Click a behavior** → in addition to the panel, tier 1 dims and the neuron/synapse chips whose text is in the behavior's `uses` glow.
- **Default on load** → the AI module is pinned, so the facets and model config are visible immediately (this is the "show the AI example" requirement).
- **Keyboard** → every node is focusable (`tabindex`/native `<button>`); focus triggers the same detail as hover; Enter/Space activate. Visible focus ring.
- **Reduced motion** → all transitions and the connector signal animation are disabled under `prefers-reduced-motion`.

## 8. VitePress integration

**Theme registration.** `docs/.vitepress/theme/index.js`:

```js
import DefaultTheme from 'vitepress/theme'
import ArchitectureMap from './ArchitectureMap.vue'
export default {
  extends: DefaultTheme,
  enhanceApp({ app }) { app.component('ArchitectureMap', ArchitectureMap) }
}
```

**Embedding.** Add `<ArchitectureMap />` on its own line in `docs/architecture.md` immediately after the §1 The vision prose. VitePress renders registered components inline in markdown.

**Theming — the one real difference from the prototype.** The prototype keys dark mode off `prefers-color-scheme` and a `data-theme` attribute. VitePress instead toggles an `html.dark` class and exposes `--vp-c-*` custom properties. The component must therefore: derive ground, surface, text, and border tokens from VitePress's `--vp-c-bg`, `--vp-c-bg-soft`, `--vp-c-text-1/2`, `--vp-c-divider` where sensible, and define the status/accent/fact colors as scoped tokens with an `html.dark` override rather than a media query. This makes the diagram track the site's own theme toggle exactly.

**Anchors.** The `section` values are in-page hashes (the component lives in `architecture.md`). VitePress slugifies headings; the exact slug for each `### 4.x <Name>` heading must be read from the built output and written into the data, not guessed. Verify each of the seven links resolves during implementation.

## 9. Testing and verification

The docs gate is `node:test` structural assertions plus a real `vitepress build`, not a Vue DOM harness — so tests cover structure, data integrity, and a clean build, and interaction is verified by building and clicking.

1. **Replace the theme guard.** `site.test.mjs:47` currently asserts `.vitepress/theme` does not exist. Replace with assertions that the theme dir exists, `index.js` registers `ArchitectureMap`, and both `ArchitectureMap.vue` and `architecture-data.js` exist.
2. **Data-integrity test (the anti-drift guard).** A new `node:test` reads `architecture-data.js` and asserts: every module `id` in the data is one of the seven real module ids; every behavior `uses` token appears as a neuron or synapse somewhere in the module data (so the highlight can never reference a chip that doesn't exist); and the module list is non-empty. This is the guard that keeps the diagram honest as the code evolves.
3. **Embedding test.** Assert `architecture.md` contains `<ArchitectureMap />`.
4. **Build.** `vitepress build` must succeed with the component and emit no dead links (the seven in-page anchors must resolve). Run it with the trimmed-PATH workaround documented for this machine.
5. **Full docs gate green** — `node tools/render-specification.mjs` then `node --test tests/*.test.mjs` — 16 existing tests plus the new ones, zero failures.

No `.cs` changes, so the .NET root gate is unaffected and is quoted, not re-derived.

## 10. Risks

- **Data drift.** Hand-authored data can fall out of step with the code (e.g. a renamed contract). Mitigation: the §9 data-integrity test ties behavior `uses` to the module vocabulary, and the module/contract names are few and stable. A generator is deliberately deferred, not pretended.
- **Theme mismatch.** If the component defines its own ground/text colors instead of deriving from `--vp-c-*`, it will clash with the site in one theme. Mitigation is explicit in §8; the build-and-look step in both themes catches it.
- **Anchor rot.** Guessed slugs would produce dead in-page links. Mitigation: read the real slugs from the build; the build's dead-link check fails otherwise.
- **Page weight.** A large component atop ~900 lines of prose. Accepted — it sits above the fold as an overview; the prose remains the reference below it. If it proves too heavy, moving it to its own nav page is a cheap follow-up, but the approved decision is to embed in `architecture.md`.

## 11. Open, deferred

- Generating `architecture-data.js` from the registry instead of hand-authoring — deferred until the registry exists.
- Showing synapses *flowing* between neurons at the kernel level (an animated substrate view) — a possible later enhancement, explicitly not in this version.
- A dedicated nav entry / standalone map page — only if embedding proves too heavy.
