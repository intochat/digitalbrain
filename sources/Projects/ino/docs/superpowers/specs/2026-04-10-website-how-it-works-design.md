# Website: Landing cleanup + "How it works" docs page

## Summary

Two scoped changes to the ino website (`website/`):

1. **Landing page** — remove top navbar, rewire two hero buttons
2. **New docs page** — `/guide/how-it-works` with interactive network diagram focused on neurons, synapses, and BDD-guaranteed behavior

## Change 1: Landing page (minimal)

### What changes

1. **Hide VitePress top navbar on homepage only.** The `HomePage.vue` already applies `body.ino-homepage` class on mount. Add CSS to hide `.VPNav` on that class, matching the existing pattern used for `.VPFooter` and `.VPLocalNav` in `custom.css`.

2. **"Explore INO" button** — change from `<a :href="withBase('/guide/')">` to a click handler that smooth-scrolls to `.ino-primitives` section.

3. **"How it works →" button** — change from `<a :href="withBase('/guide/architecture')">` to `<a :href="withBase('/guide/how-it-works')">`.

### What does NOT change

Hero section, mesh animation, primitives section, grows section, time travel section, future section, closing section, all CSS, all animations. Zero visual changes beyond the navbar removal and button targets.

### Files to modify

| File | Change |
|------|--------|
| `website/.vitepress/theme/custom.css` | Add `body.ino-homepage .VPNav { display: none !important; }` |
| `website/.vitepress/theme/components/HomePage.vue` | Rewire "Explore INO" to `scrollIntoView({ behavior: 'smooth' })` on `.ino-primitives`. Rewire "How it works" href to `/guide/how-it-works`. |

## Change 2: New docs page — `/guide/how-it-works`

### URL and navigation

- Page: `website/guide/how-it-works.md`
- Add to sidebar in `config.mts` under "Introduction" group, between "Getting Started" and "Examples"
- Standard VitePress docs layout with sidebar — NOT immersive/standalone

### Page structure — three zones

#### Zone 1: Interactive network diagram (Vue component)

A new `HowItWorksDiagram.vue` component (`website/.vitepress/theme/components/`). Brain-like topology rendered as an inline SVG.

**Neurons** (nodes):
- Circles with label + short description
- Show real ino neurons: `assistant` (central, larger), `shell`, `git`, `roslyn`, `dotnet`, `nuget`
- Hover highlights the neuron and its connected synapses; click locks the selection (same UX pattern as existing `ArchitectureDiagram.vue`)

**Synapses** (edges):
- Dashed lines connecting neuron pairs, labeled with the verb (`delegate`, `commit`, `analyze`, etc.)
- Animated pulse dots traveling along connections (SVG `animateMotion`)
- Hover a synapse → description panel shows verb, source→target, and decay concept

**Behavior badges** (floating above):
- Green-tinted rounded rects showing composed behaviors: `code-review`, `auto-commit`, etc.
- Each badge represents neuron + synapse composition
- **Click a behavior badge → inline BDD panel** slides in below the diagram showing the Gherkin scenario that guarantees this behavior. Panel shows the `.feature` file content with syntax-highlighted Given/When/Then steps. Click again to close.

**Description panel** (below SVG):
- Same pattern as existing `ArchitectureDiagram.vue` — hover/click a node to see its description
- When a behavior badge is clicked, the panel switches to show the BDD scenario instead

**Visual treatment:**
- Dark background container (`var(--vp-c-bg-alt)` or similar)
- Brand indigo (`#818cf8`) for neurons, lighter indigo (`#a5b4fc`) for synapses, green (`#6ee7b7`) for behavior badges
- Entrance animations on scroll (IntersectionObserver, matching existing pattern)

#### Zone 2: Written explanation sections

Standard markdown content below the diagram component. Three sections:

**Neurons** — Small, specialized intelligence units. Each neuron is an expert at a single thing. Created at runtime via `ino create <name>`. Links to the ino new feature concepts.

**Synapses** — Directed connections between neurons carrying a verb. A synapse is simultaneously signal (message delivery), memory (decay score 0-100), and thinking (executable code). Connected via `ino connect <src> <dst> <verb>`.

**Behavior = Neuron + Synapse** — When neurons connect via synapses, behavior emerges. "code-review" isn't a monolith — it's `roslyn` + `git` + the `analyze` synapse between them. Behaviors are compositional, not prescribed.

#### Zone 3: "Guaranteed by BDD" section

Dedicated section with green accent color. Explains:

- Every behavior is backed by a Gherkin `.feature` file
- One feature file per neuron, one scenario per synapse verb
- If the test passes, the behavior is real. If it doesn't, the behavior doesn't ship.

Includes a full code block showing an example `.feature` file with multiple scenarios, syntax-highlighted. References the actual test files in `features/ino-new/InoNew.Tests/Features/`.

### Files to create

| File | Purpose |
|------|---------|
| `website/guide/how-it-works.md` | The docs page — frontmatter + `<HowItWorksDiagram />` + markdown sections |
| `website/.vitepress/theme/components/HowItWorksDiagram.vue` | Interactive network diagram Vue component |

### Files to modify

| File | Change |
|------|--------|
| `website/.vitepress/config.mts` | Add `{ text: 'How it works', link: '/guide/how-it-works' }` to sidebar Introduction group |
| `website/.vitepress/theme/index.ts` | Register `HowItWorksDiagram` component |

## Component architecture — HowItWorksDiagram.vue

Follows the same patterns as existing `ArchitectureDiagram.vue`:

- `<script setup lang="ts">` with typed node/arrow definitions
- `ref` for `hoveredNode`, `lockedNode`, `activeNode` (computed)
- `IntersectionObserver` for entrance animation
- `onEnter` / `onLeave` / `onClick` handlers
- SVG `viewBox` with responsive scaling
- Scoped CSS with VitePress CSS variables

New additions beyond ArchitectureDiagram:
- `behaviorBadges` array linking behaviors to their constituent neurons + synapse verbs
- `activeBehavior` ref for tracking which behavior badge is clicked
- `bddScenarios` map from behavior name to Gherkin scenario text
- Inline BDD panel with toggle visibility

## Design decisions

- **Reuses existing ArchitectureDiagram patterns** — same hover/click/lock UX, same CSS variable usage, same entrance animation approach. Not a rewrite, a sibling component.
- **Real neuron names from ino new** — `shell`, `git`, `roslyn`, `dotnet`, `nuget`, `assistant`. Not abstract placeholders.
- **BDD scenarios shown are representative** — based on the actual `.feature` files in `features/ino-new/InoNew.Tests/Features/` but simplified for the docs page.
- **No changes to existing architecture page** — `/guide/architecture` stays as-is with its infrastructure-focused diagram. The new page is complementary, not a replacement.
