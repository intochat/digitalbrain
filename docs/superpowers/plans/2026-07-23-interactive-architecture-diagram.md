# Interactive Architecture Diagram Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a three-tier interactive architecture diagram — a VitePress Vue component — to the DigitalBrain docs, embedded near the top of `docs/architecture.md`.

**Architecture:** A data module (`architecture-data.js`) is the single source of truth; `ArchitectureMap.vue` is the view; `theme/index.js` registers it as the docs site's first custom theme component. A `node:test` guards the data (every behavior composes only vocabulary that a module or the kernel actually ships), and the existing test that forbade a theme dir is flipped to require the component. Interaction is verified by building the site and clicking; the automated gates cover structure, data integrity, and a clean build with no dead links.

**Tech Stack:** VitePress 1.6.4, Vue 3 SFC (`<script setup>`), Node 24 `node:test`, zero additional dependencies.

## Global Constraints

- Spec of record: `docs/superpowers/specs/2026-07-23-interactive-architecture-diagram-design.md`.
- No new npm dependency. `docs/package.json` stays at `vitepress@^1.6.4` and nothing else.
- No changes to any `.cs`, `.csproj`, `.slnx`, or `Directory.Packages.props`. The .NET root gate is unaffected and is quoted, not re-derived.
- No changes to the architecture prose; the only edit to `architecture.md` is adding the `<ArchitectureMap />` tag once.
- Theme-aware via VitePress: dark mode is `html.dark`, not `prefers-color-scheme`; derive ground/surface/text/border from `--vp-c-*` and define only the accent/status/fact colors as scoped tokens with an `html.dark` override.
- Keyboard-accessible (every node a `<button>` or `tabindex`, visible focus), and all motion disabled under `prefers-reduced-motion`.
- **This machine's PATH exceeds cmd.exe's limit**, so `npm`/`node` shelling through cmd fails. Run every node/npm command from PowerShell with a trimmed PATH:
  ```powershell
  $env:PATH = "C:\Program Files\nodejs;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem"
  ```
  `docs/node_modules` is already installed in this workspace; a fresh clone needs `npm ci --no-audit --no-fund` first, with that PATH.
- Branch: `agent/gmail-salesforce-enrichment`. Do not branch, merge, or push. Commit at each task boundary with the three diff-grill answers in the body.
- The reference prototype (approved, rendered this session) is at
  `C:\Users\vhorb\AppData\Local\Temp\claude\E--intochat\7d6fa69e-3c22-4791-bffa-fd6a13fbeeb1\scratchpad\diagram-layered.html`.
  This plan carries the complete final code; the prototype is only a visual cross-check.

---

## File Structure

**Created**

| Path | Responsibility |
|---|---|
| `docs/.vitepress/theme/architecture-data.js` | The diagram's content: `KERNEL`, `MODULES`, `ACTORS`, `BEHAVIORS`. Pure data, no view logic. |
| `docs/.vitepress/theme/ArchitectureMap.vue` | The view: three tiers + detail panel, hover/click/pin state, the behavior-compose highlight. |
| `docs/.vitepress/theme/index.js` | Extends VitePress `DefaultTheme`; registers `ArchitectureMap` globally. |
| `docs/tests/architecture-data.test.mjs` | The data-integrity guard. |

**Modified**

| Path | Change |
|---|---|
| `docs/tests/site.test.mjs` | Replace the `.vitepress/theme` must-not-exist assertion (line 47) with positive assertions; add an embed assertion. |
| `docs/architecture.md` | Add `<ArchitectureMap />` once, immediately after §1 The vision. |

---

## Task 1: The data module and its integrity guard

**Files:**
- Create: `docs/tests/architecture-data.test.mjs`
- Create: `docs/.vitepress/theme/architecture-data.js`

**Interfaces:**
- Consumes: nothing.
- Produces: `KERNEL`, `MODULES`, `ACTORS`, `BEHAVIORS` (ESM named exports). `MODULES[i]` = `{ id, label, status, section, role, neurons[], synapses[], mcp, ui, aspire[], example? }`; `aspire[j]` = `{ res, sub, model, params[] }`. `BEHAVIORS[i]` = `{ id, label, status, trigger, uses[], role, script }`. `ACTORS[i]` = `{ id, label, status, role }`. `KERNEL` = `{ label, role, section, owns[], synapses[] }`. Task 2 imports all four.

- [ ] **Step 1: Record the session snapshot**

```powershell
git rev-parse HEAD
git status --porcelain
```
Expected: a clean tree (ignored `.superpowers/` scratch aside). If something else is dirty, stop and surface it.

- [ ] **Step 2: Write the failing data-integrity test**

Create `docs/tests/architecture-data.test.mjs`:

```javascript
import assert from 'node:assert/strict'
import test from 'node:test'
import { KERNEL, MODULES, ACTORS, BEHAVIORS } from '../.vitepress/theme/architecture-data.js'

const MODULE_IDS = ['ai', 'tasks', 'google', 'salesforce', 'time', 'flutter', 'memory']

test('every module is a known module, correctly typed, with an in-page section link', () => {
  assert.equal(MODULES.length, MODULE_IDS.length)
  for (const m of MODULES) {
    assert.ok(MODULE_IDS.includes(m.id), `unknown module id: ${m.id}`)
    assert.ok(['built', 'designed', 'scope'].includes(m.status), `${m.id} has a bad status`)
    assert.match(m.section, /^#[\w-]+$/, `${m.id} section must be an in-page anchor`)
    assert.ok(Array.isArray(m.neurons) && Array.isArray(m.synapses) && Array.isArray(m.aspire))
    for (const a of m.aspire) {
      assert.ok(a.res && Array.isArray(a.params), `${m.id} aspire entry malformed`)
    }
  }
})

test('every behaviour composes only vocabulary that a module or the kernel actually ships', () => {
  const vocab = new Set(KERNEL.owns)
  for (const m of MODULES) {
    m.neurons.forEach(n => vocab.add(n))
    m.synapses.forEach(s => vocab.add(s))
  }
  assert.ok(BEHAVIORS.length > 0)
  for (const b of BEHAVIORS) {
    assert.ok(b.uses.length > 0, `behaviour ${b.id} composes nothing`)
    assert.ok(b.script.includes('Behavior'), `behaviour ${b.id} script must show the Behavior base`)
    for (const token of b.uses) {
      assert.ok(vocab.has(token), `behaviour ${b.id} composes ${token}, which nothing ships`)
    }
  }
})

test('the actors and the kernel are present and typed', () => {
  assert.deepEqual(ACTORS.map(a => a.id), ['people', 'agents'])
  for (const a of ACTORS) assert.ok(['built', 'designed'].includes(a.status))
  assert.ok(KERNEL.owns.includes('Neuron') && KERNEL.owns.includes('Synapse'))
  assert.match(KERNEL.section, /^#[\w-]+$/)
})
```

- [ ] **Step 3: Run it and watch it fail**

```powershell
$env:PATH = "C:\Program Files\nodejs;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem"
Set-Location docs
node --test tests/architecture-data.test.mjs
Set-Location ..
```
Expected: FAIL — `Cannot find module ...architecture-data.js`.

- [ ] **Step 4: Create the data module**

Create `docs/.vitepress/theme/architecture-data.js`:

```javascript
export const KERNEL = {
  label: 'Kernel',
  section: '#2-the-kernel',
  role: 'DigitalBrain.Kernel.Neuron mechanics only — receive and dispatch synapses, journal both directions, enforce owner and delivery invariants, mint the one opaque CapabilityDelegation. No AI, provider, integration, or memory concepts live here.',
  owns: ['Neuron', 'Synapse', 'CapabilityDelegation'],
  synapses: ['CapabilityRequested', 'CapabilityCompleted'],
}

export const MODULES = [
  {
    id: 'ai', label: 'AI', status: 'built', section: '#41-ai',
    role: 'MAF-backed agents and orchestration over typed models. The public wire is Microsoft.Extensions.AI; MAF types stay internal.',
    neurons: ['ILLM', 'IAgent', 'IGroupChat', 'ILlama32', 'IGpt56'], synapses: [],
    mcp: false, ui: false,
    aspire: [
      { res: 'Ollama', sub: 'data volume', model: 'llama3.2', params: [] },
      { res: 'OpenAI', sub: '', model: 'gpt-5.6', params: ['openai-api-key'] },
    ],
    example: true,
  },
  {
    id: 'tasks', label: 'Tasks', status: 'built', section: '#42-tasks',
    role: 'Durable desired-outcome identity. Exactly one Attempt is active at a time; a MAF workflow runs each attempt. Workers report typed facts.',
    neurons: ['ITask', 'IWorker'],
    synapses: ['AttemptSucceeded', 'AttemptFailed', 'AttemptWaiting', 'ApprovalRequired', 'AttemptOutcomeUncertain'],
    mcp: false, ui: false, aspire: [],
  },
  {
    id: 'google', label: 'Google', status: 'built', section: '#43-google',
    role: 'Gmail as a semantic capability root. The pinned MCP catalog stays module-private; the model sees only selected exact tools.',
    neurons: ['IGmail'], synapses: [], mcp: true, ui: false,
    aspire: [{ res: 'Google OAuth', sub: 'loopback callback', model: '', params: ['google-client-id', 'google-client-secret', 'google-redirect-uri'] }],
  },
  {
    id: 'salesforce', label: 'Salesforce', status: 'built', section: '#44-salesforce',
    role: 'Approved, reconciled external mutations bound to a CommandId. Never claims exactly-once effects.',
    neurons: ['ISalesforce'], synapses: [], mcp: true, ui: false,
    aspire: [{ res: 'Salesforce OAuth', sub: 'external client app', model: '', params: ['salesforce-client-id', 'salesforce-client-secret', 'salesforce-redirect-uri'] }],
  },
  {
    id: 'time', label: 'Time', status: 'designed', section: '#45-time',
    role: 'Durable one-shot and recurring schedules, separate from the kernel-private outbox timers. Reuses the shared kernel reminder provider — it adds no store of its own.',
    neurons: ['ICountdown', 'IReminder'],
    synapses: ['CountdownElapsed', 'ReminderElapsed', 'ReminderOverdue'],
    mcp: false, ui: false, aspire: [],
  },
  {
    id: 'flutter', label: 'Flutter', status: 'designed', section: '#46-flutter',
    role: 'Flutter neurons and a contract drift guard. Outside the first executable proof.',
    neurons: ['IFlutter'], synapses: [], mcp: false, ui: true, aspire: [],
  },
  {
    id: 'memory', label: 'Memory', status: 'scope', section: '#47-memory',
    role: 'Deliberately out of scope. Designed independently around its own vocabulary, later.',
    neurons: [], synapses: [], mcp: false, ui: false, aspire: [],
  },
]

export const ACTORS = [
  {
    id: 'people', label: 'People', status: 'built',
    role: 'Operate the brain through the owner-bound client — DigitalBrainClient.Connect(grains, "acme"). They send to and observe neurons, and they are the approval authority for every behaviour install.',
  },
  {
    id: 'agents', label: 'Agents', status: 'designed',
    role: 'LLM-powered neurons that also act inside the workspace. An agent can propose a behaviour or operate a neuron, but a mutating action still passes through the same human approval rail — an agent advises, it never owns authority.',
  },
]

export const BEHAVIORS = [
  {
    id: 'digest', label: 'Morning digest', status: 'designed', trigger: 'on ReminderElapsed',
    uses: ['ReminderElapsed', 'IReminder', 'IGmail', 'ILLM'],
    role: 'When the daily reminder elapses, read unread mail and summarise it with a local model. Composes Time, Google, and AI vocabulary — no new contract.',
    script: `public sealed class MorningDigest(
    IReminder daily, IGmail gmail, ILlama32 llama)
    : Behavior, IHandle<ReminderElapsed>
{
    public async Task HandleAsync(ReminderElapsed e, ...)
    {
        var mail = await gmail.SearchAsync("is:unread");
        await llama.Respond(Digest(mail));
    }
}`,
  },
  {
    id: 'lead', label: 'Lead follow-up', status: 'designed', trigger: 'on ApprovalRequired',
    uses: ['ApprovalRequired', 'ISalesforce', 'ICountdown'],
    role: 'When a Task asks for approval, update the Salesforce record and set a follow-up countdown. Composes Tasks, Salesforce, and Time — the mutation stays behind the module’s approval rail.',
    script: `public sealed class LeadFollowUp(
    ISalesforce crm, ICountdown followUp)
    : Behavior, IHandle<ApprovalRequired>
{
    public async Task HandleAsync(ApprovalRequired a, ...)
    {
        await crm.UpdateAsync(a.Account, ...);
        await followUp.StartAsync(TimeSpan.FromDays(2));
    }
}`,
  },
]
```

- [ ] **Step 5: Run the data test and watch it pass**

```powershell
$env:PATH = "C:\Program Files\nodejs;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem"
Set-Location docs
node --test tests/architecture-data.test.mjs
Set-Location ..
```
Expected: PASS — 3 tests.

- [ ] **Step 6: Run the full docs test suite**

```powershell
Set-Location docs
node tools/render-specification.mjs
node --test tests/*.test.mjs
Set-Location ..
```
Expected: the data file now lives in `docs/.vitepress/theme/`, so that directory exists and the site test's old "theme must not exist" assertion would fail. As part of this task, flip that one line in `docs/tests/site.test.mjs` from `false` to `true` (assert the theme directory exists) with a `// the interactive architecture diagram lives in the site's one custom theme` comment above it, so the boundary stays green. Task 2 strengthens it to require the component files. Then the 16 existing tests plus the 3 new ones pass — quote the pass count. Commit the site.test.mjs edit with the two new files.

- [ ] **Step 7: Commit**

```powershell
git add docs/.vitepress/theme/architecture-data.js docs/tests/architecture-data.test.mjs
git commit -m "docs: add the architecture diagram data and its integrity guard"
```

---

## Task 2: The component and theme registration

**Files:**
- Create: `docs/.vitepress/theme/ArchitectureMap.vue`
- Create: `docs/.vitepress/theme/index.js`
- Modify: `docs/tests/site.test.mjs` (replace the line-47 guard)

**Interfaces:**
- Consumes: `KERNEL`, `MODULES`, `ACTORS`, `BEHAVIORS` from `architecture-data.js` (Task 1).
- Produces: a globally registered `<ArchitectureMap />` component. Task 3 embeds the tag.

- [ ] **Step 1: Strengthen the theme guard to a failing positive assertion**

Task 1 already flipped the guard to `assert.equal(existsSync(join(docsRoot, '.vitepress', 'theme')), true)` (with a `// the interactive architecture diagram lives in the site's one custom theme` comment). In `docs/tests/site.test.mjs`, replace those two lines inside the `every documented page exists…` test:

```javascript
  // the interactive architecture diagram lives in the site's one custom theme
  assert.equal(existsSync(join(docsRoot, '.vitepress', 'theme')), true)
```

with the stronger assertions:

```javascript
  // the interactive architecture diagram lives in the site's one custom theme
  assert.equal(existsSync(join(docsRoot, '.vitepress', 'theme', 'index.js')), true)
  assert.equal(existsSync(join(docsRoot, '.vitepress', 'theme', 'ArchitectureMap.vue')), true)
  const themeIndex = read('docs', '.vitepress', 'theme', 'index.js')
  assert.match(themeIndex, /extends:\s*DefaultTheme/)
  assert.match(themeIndex, /app\.component\('ArchitectureMap'/)
```

- [ ] **Step 2: Run the docs suite and watch the guard fail**

```powershell
$env:PATH = "C:\Program Files\nodejs;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem"
Set-Location docs
node --test tests/*.test.mjs
Set-Location ..
```
Expected: FAIL — `.vitepress/theme/index.js` does not exist yet.

- [ ] **Step 3: Create the theme registration**

Create `docs/.vitepress/theme/index.js`:

```javascript
import DefaultTheme from 'vitepress/theme'
import ArchitectureMap from './ArchitectureMap.vue'

export default {
  extends: DefaultTheme,
  enhanceApp({ app }) {
    app.component('ArchitectureMap', ArchitectureMap)
  },
}
```

- [ ] **Step 4: Create the component**

Create `docs/.vitepress/theme/ArchitectureMap.vue` with exactly this content:

```vue
<script setup>
import { ref, computed } from 'vue'
import { KERNEL, MODULES, ACTORS, BEHAVIORS } from './architecture-data.js'

const SLABEL = { built: 'Built', designed: 'Designed', scope: 'Out of scope' }
const DOT = { built: 'b', designed: 'd', scope: 's' }

const pinned = ref('mod:ai') // default: AI open so the facets and model config show on load
const hovered = ref(null)
const activeKey = computed(() => pinned.value ?? hovered.value)

const activeModule = computed(() =>
  activeKey.value?.startsWith('mod:') ? MODULES.find(m => 'mod:' + m.id === activeKey.value) : null)
const activeBehavior = computed(() =>
  activeKey.value?.startsWith('beh:') ? BEHAVIORS.find(b => 'beh:' + b.id === activeKey.value) : null)
const activeActor = computed(() => ACTORS.find(a => a.id === activeKey.value) ?? null)
const activeKernel = computed(() => activeKey.value === 'kernel')

const glow = computed(() => new Set(activeBehavior.value?.uses ?? []))
const dimVocab = computed(() => activeBehavior.value != null)
const flows = computed(() => {
  if (activeKernel.value) return ['t1-t2']
  if (activeBehavior.value || activeActor.value) return ['t1-t2', 't2-t3']
  return []
})

const facetsOf = m => {
  const f = []
  if (m.mcp) f.push('MCP')
  if (m.aspire.length) f.push('Aspire')
  if (m.ui) f.push('UI')
  return f
}
const isSynapse = t => /^[A-Z]/.test(t) && !t.startsWith('I')

const enter = key => { if (!pinned.value) hovered.value = key }
const leave = () => { hovered.value = null }
const toggle = key => { pinned.value = pinned.value === key ? null : key; hovered.value = null }
</script>

<template>
  <div class="arch-map">
    <div class="legend">
      <span><i class="dot k" /> Kernel</span>
      <span><i class="dot b" /> Built</span>
      <span><i class="dot d" /> Designed</span>
      <span><i class="dot s" /> Out of scope</span>
      <span><i class="dot f" /> Synapse (fact)</span>
    </div>

    <div class="layout">
      <div class="tiers">
        <!-- TIER 1 -->
        <section class="tier">
          <div class="tier-label"><span class="tier-num">01</span><h3>DigitalBrain</h3><span class="tl-sub">the substrate and everything a module ships — compile-time</span></div>
          <div class="platform">
            <button class="kernel-strip" :class="{ sel: activeKernel }"
              @mouseenter="enter('kernel')" @mouseleave="leave" @focus="enter('kernel')" @click="toggle('kernel')">
              <span class="kn"><i class="dot k" /> Kernel</span>
              <span class="ks-sub">routes synapses · dual journals · owner &amp; delivery invariants · one opaque delegation</span>
              <span class="ks-chips"><span v-for="t in KERNEL.synapses" :key="t" class="chip synapse">{{ t }}</span></span>
            </button>
            <div class="modgrid">
              <button v-for="m in MODULES" :key="m.id" class="mod" :class="{ sel: activeKey === 'mod:' + m.id }"
                :style="{ '--edge': 'var(--' + m.status + ')' }"
                @mouseenter="enter('mod:' + m.id)" @mouseleave="leave" @focus="enter('mod:' + m.id)" @click="toggle('mod:' + m.id)">
                <span class="mod-name">{{ m.label }}<span class="mod-status">{{ m.status === 'scope' ? 'scope' : SLABEL[m.status] }}</span></span>
                <div class="vocab" :class="{ dim: dimVocab }">
                  <p class="vlabel"><i class="dot n" /> Neurons</p>
                  <div v-if="m.neurons.length" class="chips">
                    <span v-for="t in m.neurons" :key="t" class="chip neuron" :class="{ glow: glow.has(t) }">{{ t }}</span>
                  </div>
                  <span v-else class="none">none</span>
                  <p class="vlabel"><i class="dot f" /> Synapses</p>
                  <div v-if="m.synapses.length" class="chips">
                    <span v-for="t in m.synapses" :key="t" class="chip synapse" :class="{ glow: glow.has(t) }">{{ t }}</span>
                  </div>
                  <span v-else class="none">none</span>
                </div>
                <div v-if="facetsOf(m).length" class="ships-footer">
                  <span class="sf-lbl">also ships</span>
                  <span v-for="f in facetsOf(m)" :key="f" class="fpill">{{ f }}</span>
                </div>
              </button>
            </div>
          </div>
        </section>

        <div class="tconnect" :class="{ hot: flows.includes('t1-t2') }"><span class="track" />actors operate the vocabulary ↑↓</div>

        <!-- TIER 2 -->
        <section class="tier">
          <div class="tier-label"><span class="tier-num">02</span><h3>People &amp; Agents</h3><span class="tl-sub">the actors in a workspace — owner "acme"</span></div>
          <div class="actors-tier">
            <button v-for="a in ACTORS" :key="a.id" class="actor" :class="{ sel: activeKey === a.id }"
              @mouseenter="enter(a.id)" @mouseleave="leave" @focus="enter(a.id)" @click="toggle(a.id)">
              <span class="a-top"><i class="dot" :class="DOT[a.status]" /> {{ a.label }}
                <span class="badge a-badge" :class="a.status">{{ SLABEL[a.status] }}</span></span>
              <p class="a-role">{{ a.role }}</p>
            </button>
          </div>
        </section>

        <div class="tconnect" :class="{ hot: flows.includes('t2-t3') }"><span class="track" />author ↓</div>

        <!-- TIER 3 -->
        <section class="tier">
          <div class="tier-label"><span class="tier-num">03</span><h3>Behaviors — scripting</h3><span class="tl-sub">runtime · one C# file · human-approved install</span></div>
          <div class="behaviors-tier">
            <p class="beh-hint">A behaviour is one public <b>Behavior</b> class composing existing typed vocabulary. <b>Select one to see what it composes.</b></p>
            <div class="behgrid">
              <button v-for="b in BEHAVIORS" :key="b.id" class="beh" :class="{ sel: activeKey === 'beh:' + b.id }"
                @mouseenter="enter('beh:' + b.id)" @mouseleave="leave" @focus="enter('beh:' + b.id)" @click="toggle('beh:' + b.id)">
                <span class="beh-name">{{ b.label }}</span>
                <p class="beh-trig">{{ b.trigger }}</p>
                <div class="beh-uses">
                  <span v-for="t in b.uses" :key="t" class="chip" :class="isSynapse(t) ? 'synapse' : 'neuron'">{{ t }}</span>
                </div>
              </button>
            </div>
          </div>
        </section>
      </div>

      <!-- PANEL -->
      <aside class="panel">
        <template v-if="activeModule">
          <div class="p-head"><i class="dot" :class="DOT[activeModule.status]" /><h4>{{ activeModule.label }}</h4>
            <span class="badge" :class="activeModule.status">{{ SLABEL[activeModule.status] }}</span></div>
          <p class="p-kind">module · ships onto the kernel</p>
          <p class="p-role">{{ activeModule.role }}</p>

          <p class="p-sec">Neurons — capabilities</p>
          <div v-if="activeModule.neurons.length" class="chips"><span v-for="t in activeModule.neurons" :key="t" class="chip neuron">{{ t }}</span></div>
          <span v-else class="none">none yet</span>

          <p class="p-sec" :class="{ off: !activeModule.synapses.length }">Synapses — typed facts</p>
          <div v-if="activeModule.synapses.length" class="chips"><span v-for="t in activeModule.synapses" :key="t" class="chip synapse">{{ t }}</span></div>
          <span v-else class="none">none</span>

          <p class="p-sec" :class="{ off: !activeModule.mcp }">MCP tools{{ activeModule.mcp ? '' : ' — none' }}</p>
          <p v-if="activeModule.mcp" class="p-note">A private pinned catalog. Projected to the model as selected exact tools — the raw MCP client, tool names, and JSON never cross the module contract.</p>

          <p class="p-sec" :class="{ off: !activeModule.aspire.length }">Aspire hosting{{ activeModule.aspire.length ? '' : ' — none' }}</p>
          <div v-for="a in activeModule.aspire" :key="a.res" class="res">
            <b>{{ a.res }}</b><span v-if="a.sub" class="arrow"> · {{ a.sub }}</span><span v-if="a.model"> → <span class="mono">{{ a.model }}</span></span>
            <div v-if="a.params.length" class="chips param-row">
              <span v-for="p in a.params" :key="p" class="chip param secret">{{ p }}</span>
            </div>
          </div>

          <p class="p-sec" :class="{ off: !activeModule.ui }">UI{{ activeModule.ui ? '' : ' — none' }}</p>
          <p v-if="activeModule.ui" class="p-note">A Flutter surface — designed, not built.</p>

          <template v-if="activeModule.example">
            <p class="p-sec">Model configuration</p>
            <pre class="code">brain.AddModule&lt;AIModule&gt;(ai =&gt; ai
    .WithLlm&lt;Llama32&gt;()   // Ollama · llama3.2
    .WithLlm&lt;Gpt56&gt;());  // OpenAI · gpt-5.6 · 🔒 openai-api-key</pre>
          </template>

          <a class="p-jump" :href="activeModule.section">Read the {{ activeModule.label }} section →</a>
        </template>

        <template v-else-if="activeBehavior">
          <div class="p-head"><h4>{{ activeBehavior.label }}</h4><span class="badge designed">behaviour</span></div>
          <p class="p-kind">scripting · composes {{ activeBehavior.uses.length }} pieces of vocabulary</p>
          <p class="p-role">{{ activeBehavior.role }}</p>
          <p class="p-sec">Composes</p>
          <div class="chips"><span v-for="t in activeBehavior.uses" :key="t" class="chip" :class="isSynapse(t) ? 'synapse' : 'neuron'">{{ t }}</span></div>
          <p class="p-sec">The whole file</p>
          <pre class="code">{{ activeBehavior.script }}</pre>
          <a class="p-jump" href="#5-behaviors-and-scripting">Read Behaviors →</a>
        </template>

        <template v-else-if="activeActor">
          <div class="p-head"><i class="dot" :class="DOT[activeActor.status]" /><h4>{{ activeActor.label }}</h4>
            <span class="badge" :class="activeActor.status">{{ SLABEL[activeActor.status] }}</span></div>
          <p class="p-kind">actor · in the workspace</p>
          <p class="p-role">{{ activeActor.role }}</p>
        </template>

        <template v-else-if="activeKernel">
          <div class="p-head"><i class="dot k" /><h4>{{ KERNEL.label }}</h4><span class="badge core">Built</span></div>
          <p class="p-kind">neuron substrate</p>
          <p class="p-role">{{ KERNEL.role }}</p>
          <p class="p-sec">Owns</p>
          <div class="chips"><span v-for="t in KERNEL.owns" :key="t" class="chip neuron">{{ t }}</span></div>
          <a class="p-jump" :href="KERNEL.section">Read the Kernel section →</a>
        </template>

        <p v-else class="p-empty">Hover the platform, the actors, or a behaviour. Click a module for everything it ships, or a behaviour to light up the exact neurons and synapses it composes.</p>
      </aside>
    </div>
  </div>
</template>

<style scoped>
.arch-map {
  --ground: var(--vp-c-bg);
  --board: var(--vp-c-bg);
  --panel: var(--vp-c-bg-soft);
  --sunk: var(--vp-c-bg-soft);
  --ink: var(--vp-c-text-1);
  --muted: var(--vp-c-text-2);
  --faint: var(--vp-c-text-3);
  --hair: var(--vp-c-divider);
  --accent: #0891b2; --built: #059669; --designed: #d97706; --scope: #64748b; --fact: #7c3aed;
  --shadow: 0 1px 2px rgba(20, 30, 60, .06), 0 8px 26px rgba(20, 30, 60, .08);
  margin: 20px 0 8px;
  font-size: 14px;
  color: var(--ink);
}
:global(html.dark) .arch-map {
  --accent: #22d3ee; --built: #34d399; --designed: #fbbf24; --scope: #7c89a3; --fact: #a78bfa;
  --shadow: 0 1px 2px rgba(0, 0, 0, .4), 0 12px 40px rgba(0, 0, 0, .4);
}
.arch-map .mono { font-family: var(--vp-font-family-mono, ui-monospace, Menlo, monospace); }

.legend { display: flex; flex-wrap: wrap; gap: 7px 15px; align-items: center; font-size: 12.5px; color: var(--muted); margin-bottom: 16px; }
.legend span { display: inline-flex; align-items: center; gap: 6px; }
.dot { width: 9px; height: 9px; border-radius: 50%; flex: none; display: inline-block; }
.dot.k, .dot.n { background: var(--accent); } .dot.b { background: var(--built); }
.dot.d { background: var(--designed); } .dot.s { background: var(--scope); } .dot.f { background: var(--fact); }

.layout { display: grid; grid-template-columns: 1fr; gap: 16px; }
@media (min-width: 960px) { .layout { grid-template-columns: 1fr 330px; align-items: start; } }
.tiers { display: flex; flex-direction: column; }

.tier-label { display: flex; align-items: baseline; gap: 10px; margin: 0 2px 9px; flex-wrap: wrap; }
.tier-num { font-family: var(--vp-font-family-mono, monospace); font-size: 12px; font-weight: 700; color: var(--accent); border: 1px solid color-mix(in srgb, var(--accent) 40%, var(--hair)); border-radius: 6px; padding: 2px 7px; }
.tier-label h3 { margin: 0; font-size: 16px; border: 0; padding: 0; }
.tier-label .tl-sub { color: var(--muted); font-size: 12.5px; }

.tconnect { display: flex; align-items: center; justify-content: center; height: 34px; color: var(--faint); font-size: 11px; letter-spacing: .07em; text-transform: uppercase; font-weight: 600; position: relative; }
.tconnect .track { position: absolute; left: 50%; top: 0; bottom: 0; width: 2px; background: var(--hair); transform: translateX(-50%); }
.tconnect.hot .track { background: var(--accent); } .tconnect.hot { color: var(--accent); }

.platform { background: color-mix(in srgb, var(--accent) 5%, var(--sunk)); border: 1px solid color-mix(in srgb, var(--accent) 28%, var(--hair)); border-radius: 14px; box-shadow: var(--shadow); padding: 12px; }
.kernel-strip { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; width: 100%; text-align: left; background: var(--board); border: 1px solid color-mix(in srgb, var(--accent) 30%, var(--hair)); border-radius: 10px; padding: 10px 12px; margin-bottom: 11px; cursor: pointer; color: inherit; font: inherit; }
.kernel-strip.sel, .kernel-strip:hover { box-shadow: var(--shadow); }
.kernel-strip .kn { font-weight: 700; font-size: 14px; display: flex; align-items: center; gap: 8px; }
.kernel-strip .ks-sub { color: var(--muted); font-size: 12px; }
.kernel-strip .ks-chips { display: flex; gap: 5px; margin-left: auto; flex-wrap: wrap; }

.modgrid { display: grid; grid-template-columns: repeat(auto-fill, minmax(210px, 1fr)); gap: 9px; }
.mod { background: var(--board); border: 1px solid var(--hair); border-top: 3px solid var(--edge); border-radius: 11px; padding: 10px 11px 11px; cursor: pointer; text-align: left; font: inherit; color: inherit; width: 100%; transition: transform .16s, box-shadow .16s; }
.mod:hover, .mod:focus-visible { transform: translateY(-2px); box-shadow: var(--shadow); outline: none; }
.mod.sel { box-shadow: 0 0 0 2px color-mix(in srgb, var(--edge) 40%, transparent), var(--shadow); }
.mod-name { font-weight: 700; font-size: 14px; display: flex; align-items: center; justify-content: space-between; gap: 6px; }
.mod-status { font-size: 9.5px; font-weight: 700; letter-spacing: .04em; text-transform: uppercase; color: var(--edge); }
.vocab { margin-top: 9px; }
.vlabel { font-size: 9.5px; letter-spacing: .09em; text-transform: uppercase; color: var(--faint); margin: 0 0 4px; display: flex; align-items: center; gap: 5px; }
.chips { display: flex; flex-wrap: wrap; gap: 4px; margin-bottom: 7px; }
.chip { font-family: var(--vp-font-family-mono, monospace); font-size: 10.5px; border-radius: 5px; padding: 2px 6px; border: 1px solid var(--hair); background: var(--sunk); color: var(--muted); transition: all .18s; }
.chip.neuron { border-color: color-mix(in srgb, var(--accent) 22%, var(--hair)); color: var(--ink); }
.chip.synapse { border-color: color-mix(in srgb, var(--fact) 28%, var(--hair)); color: var(--ink); background: color-mix(in srgb, var(--fact) 6%, var(--sunk)); }
.chip.synapse::before { content: "↯"; color: var(--fact); margin-right: 3px; font-size: 9px; }
.chip.param { border-color: color-mix(in srgb, var(--designed) 35%, var(--hair)); }
.chip.param.secret::before { content: "🔒"; margin-right: 3px; font-size: 9px; }
.chip.neuron.glow { background: color-mix(in srgb, var(--accent) 20%, transparent); border-color: var(--accent); box-shadow: 0 0 0 2px color-mix(in srgb, var(--accent) 25%, transparent); }
.chip.synapse.glow { background: color-mix(in srgb, var(--fact) 22%, transparent); border-color: var(--fact); box-shadow: 0 0 0 2px color-mix(in srgb, var(--fact) 25%, transparent); }
.vocab.dim .chip:not(.glow) { opacity: .32; }
.none { color: var(--faint); font-size: 11px; font-style: italic; }
.ships-footer { display: flex; gap: 5px; flex-wrap: wrap; margin-top: 4px; padding-top: 9px; border-top: 1px dashed var(--hair); align-items: center; }
.sf-lbl { font-size: 9px; letter-spacing: .08em; text-transform: uppercase; color: var(--faint); }
.fpill { font-size: 9.5px; font-weight: 700; letter-spacing: .03em; text-transform: uppercase; padding: 2px 7px; border-radius: 999px; background: color-mix(in srgb, var(--accent) 10%, transparent); color: var(--accent); border: 1px solid color-mix(in srgb, var(--accent) 28%, var(--hair)); }

.actors-tier { background: var(--sunk); border: 1px solid var(--hair); border-radius: 14px; padding: 12px; display: flex; gap: 10px; flex-wrap: wrap; }
.actor { flex: 1 1 220px; background: var(--board); border: 1px solid var(--hair); border-radius: 11px; padding: 11px 13px; cursor: pointer; text-align: left; font: inherit; color: inherit; transition: transform .16s, box-shadow .16s, border-color .16s; }
.actor:hover, .actor:focus-visible { transform: translateY(-2px); box-shadow: var(--shadow); border-color: var(--accent); outline: none; }
.actor.sel { box-shadow: 0 0 0 2px color-mix(in srgb, var(--accent) 35%, transparent), var(--shadow); }
.a-top { display: flex; align-items: center; gap: 9px; font-weight: 700; font-size: 14px; }
.a-role { color: var(--muted); font-size: 12.5px; margin: 7px 0 0; }
.a-badge { margin-left: auto; }

.behaviors-tier { background: var(--sunk); border: 1px dashed color-mix(in srgb, var(--designed) 42%, var(--hair)); border-radius: 14px; padding: 12px; }
.beh-hint { color: var(--muted); font-size: 12.5px; margin: 0 0 10px; }
.beh-hint b { color: var(--designed); }
.behgrid { display: grid; grid-template-columns: repeat(auto-fill, minmax(228px, 1fr)); gap: 9px; }
.beh { background: var(--board); border: 1px solid var(--hair); border-left: 3px solid var(--designed); border-radius: 11px; padding: 11px 12px; cursor: pointer; text-align: left; font: inherit; color: inherit; width: 100%; transition: transform .16s, box-shadow .16s; }
.beh:hover, .beh:focus-visible { transform: translateY(-2px); box-shadow: var(--shadow); outline: none; }
.beh.sel { box-shadow: 0 0 0 2px color-mix(in srgb, var(--designed) 40%, transparent), var(--shadow); }
.beh-name { font-weight: 700; font-size: 14px; }
.beh-trig { color: var(--muted); font-size: 12px; margin: 6px 0 0; }
.beh-uses { display: flex; flex-wrap: wrap; gap: 4px; margin-top: 9px; }

.panel { background: var(--panel); border: 1px solid var(--hair); border-radius: 14px; padding: 18px; position: sticky; top: 90px; box-shadow: var(--shadow); }
.p-empty { color: var(--muted); font-size: 13.5px; margin: 0; }
.p-head { display: flex; align-items: center; gap: 9px; margin: 0 0 3px; }
.p-head h4 { margin: 0; font-size: 18px; border: 0; padding: 0; }
.p-kind { font-size: 11px; letter-spacing: .1em; text-transform: uppercase; color: var(--muted); margin: 0 0 11px; }
.p-role { color: var(--muted); font-size: 13px; margin: 0 0 6px; }
.p-sec { font-size: 10.5px; letter-spacing: .09em; text-transform: uppercase; color: var(--muted); margin: 15px 0 6px; }
.p-sec.off { color: var(--faint); }
.p-note { color: var(--muted); font-size: 12.5px; margin: 0; }
.res { background: var(--board); border: 1px solid var(--hair); border-radius: 8px; padding: 8px 10px; font-size: 12.5px; margin-bottom: 6px; }
.res .arrow { color: var(--faint); }
.param-row { margin-top: 6px; }
.code { background: var(--board); border: 1px solid var(--hair); border-radius: 9px; padding: 11px 12px; margin-top: 8px; font-family: var(--vp-font-family-mono, monospace); font-size: 11px; line-height: 1.7; overflow-x: auto; white-space: pre; color: var(--ink); }
.badge { font-size: 10px; font-weight: 700; letter-spacing: .05em; text-transform: uppercase; padding: 2px 7px; border-radius: 999px; margin-left: auto; }
.badge.built { background: color-mix(in srgb, var(--built) 16%, transparent); color: var(--built); }
.badge.designed { background: color-mix(in srgb, var(--designed) 18%, transparent); color: var(--designed); }
.badge.scope { background: color-mix(in srgb, var(--scope) 18%, transparent); color: var(--scope); }
.badge.core { background: color-mix(in srgb, var(--accent) 16%, transparent); color: var(--accent); }
.p-jump { display: inline-flex; gap: 5px; margin-top: 16px; font-size: 13.5px; font-weight: 600; color: var(--accent); text-decoration: none; }
.p-jump:hover { text-decoration: underline; }

@media (prefers-reduced-motion: reduce) { .arch-map * { transition: none !important; } }
</style>
```

- [ ] **Step 5: Run the docs suite and watch the guard pass**

```powershell
$env:PATH = "C:\Program Files\nodejs;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem"
Set-Location docs
node --test tests/*.test.mjs
Set-Location ..
```
Expected: all pass — the flipped guard now sees `index.js` and `ArchitectureMap.vue`.

- [ ] **Step 6: Build the site to prove the theme compiles**

```powershell
Set-Location docs
node tools/render-specification.mjs
npx vitepress build
Set-Location ..
```
Expected: `build complete`. The component compiles (registered but not yet embedded, so not rendered). If Vue reports a template or script error, fix the component before continuing.

- [ ] **Step 7: Commit**

```powershell
git add docs/.vitepress/theme/ArchitectureMap.vue docs/.vitepress/theme/index.js docs/tests/site.test.mjs
git commit -m "docs: add the ArchitectureMap component and register the theme"
```

---

## Task 3: Embed the diagram and verify anchors

**Files:**
- Modify: `docs/architecture.md` (add the tag after §1)
- Modify: `docs/tests/site.test.mjs` (assert the tag is embedded)

**Interfaces:**
- Consumes: the registered `<ArchitectureMap />` from Task 2.
- Produces: the rendered diagram on the architecture page.

- [ ] **Step 1: Add the embed assertion and watch it fail**

In `docs/tests/site.test.mjs`, inside the `every documented page exists…` test (right after the theme assertions from Task 2), add:

```javascript
  assert.match(read('docs', 'architecture.md'), /<ArchitectureMap\s*\/>/)
```

Run:
```powershell
$env:PATH = "C:\Program Files\nodejs;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem"
Set-Location docs
node --test tests/*.test.mjs
Set-Location ..
```
Expected: FAIL — the tag is not in `architecture.md` yet.

- [ ] **Step 2: Embed the component after §1 The vision**

`docs/architecture.md` §1 ends just before `## 2. The kernel` (currently line 31). Insert the tag on its own line, separated by blank lines, immediately before `## 2. The kernel`:

```markdown
<ArchitectureMap />

## 2. The kernel
```

Do not change any prose. The tag sits between the end of the §1 vision text and the §2 heading.

- [ ] **Step 3: Run the docs suite and watch it pass**

```powershell
Set-Location docs
node --test tests/*.test.mjs
Set-Location ..
```
Expected: all pass.

- [ ] **Step 4: Build, then verify every panel anchor resolves**

```powershell
Set-Location docs
node tools/render-specification.mjs
npx vitepress build
Set-Location ..
```
Expected: `build complete`, no dead-link warning.

Then confirm the nine anchor targets the panel links to actually exist as heading ids in the built page:

```powershell
$html = Get-Content docs/.vitepress/dist/architecture.html -Raw
foreach ($id in '2-the-kernel','41-ai','42-tasks','43-google','44-salesforce','45-time','46-flutter','47-memory','5-behaviors-and-scripting') {
  if ($html -notmatch ('id="' + $id + '"')) { Write-Error "missing anchor: $id" } else { "ok: $id" }
}
```
Expected: `ok:` for all nine. If any is missing, VitePress slugified that heading differently — read the real id from the built HTML and correct that entry's `section` in `architecture-data.js` (or the hard-coded `#5-behaviors-and-scripting` in the component's behavior panel), then rebuild until all nine resolve.

- [ ] **Step 5: Run the full docs gate**

```powershell
Set-Location docs
node tools/render-specification.mjs
node --test tests/*.test.mjs
Set-Location ..
```
Expected: zero failures. Quote the exact pass count (the 16 original + the new data tests + unchanged others).

- [ ] **Step 6: Confirm the .NET root gate is untouched**

No `.cs`, `.csproj`, `.slnx`, or `Directory.Packages.props` changed in this plan, so the root gate result is unchanged from its last green run (Tests 173/173, HostTests 5/5, Simulations 166/166). State that; do not re-run it unless a `.cs` file was touched.

- [ ] **Step 7: Commit**

```powershell
git add docs/architecture.md docs/tests/site.test.mjs
git commit -m "docs: embed the interactive architecture diagram"
```

---

## Task 4: Final verification

**Files:** none modified.

- [ ] **Step 1: Confirm the visual result in both themes**

```powershell
Set-Location docs
npx vitepress preview
```
Open the printed URL, go to the Architecture page. Confirm: the diagram renders above §2; the AI module panel is open by default showing its neurons, the Ollama/OpenAI Aspire resources, and the `openai-api-key` secret; clicking **Morning digest** dims tier 1 and lights up `ReminderElapsed`, `IReminder`, `IGmail`, `ILLM`; a module's "Read the … section →" link jumps to the right heading; toggling the site's dark-mode switch restyles the diagram cleanly. Stop the preview with Ctrl+C.

- [ ] **Step 2: Confirm the tree and the file count**

```powershell
git status --porcelain
```
Expected: clean (ignored scratch aside).

```powershell
Get-ChildItem docs/.vitepress/theme
```
Expected: exactly `architecture-data.js`, `ArchitectureMap.vue`, `index.js`.

- [ ] **Step 3: Confirm no stray dependency crept in**

```powershell
$env:PATH = "C:\Program Files\nodejs;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem"
$pkg = Get-Content docs/package.json -Raw | ConvertFrom-Json
$pkg.devDependencies
```
Expected: only `vitepress` at `^1.6.4`.

---

## Notes for the implementer

**The behavior-highlight coupling is the point, and the test protects it.** The glow works because a behavior's `uses` tokens are string-equal to neuron/synapse chip text in tier 1. Task 1's second test fails if any `uses` token isn't shipped by a module or the kernel, so you cannot silently break the highlight by editing the data. Keep that test.

**Do not hand-write syntax highlighting.** The behavior scripts and the AI config render as plain `<pre>` text — no `v-html`, no span soup. Legibility over color; this also keeps the data file pure strings.

**VitePress dark mode is a class, not a media query.** The component derives its neutral tokens from `--vp-c-*` (which VitePress flips on `html.dark`) and overrides only the five brand colors under `:global(html.dark) .arch-map`. If you find yourself writing `@media (prefers-color-scheme)`, stop — that will fight the site's own theme toggle.

**Anchors are verified, not assumed.** Task 3 Step 4 reads the real heading ids from the built HTML. The values in this plan (`#41-ai`, `#2-the-kernel`, …) are VitePress's default slugify of the current headings, but the build is the oracle — if a heading is renumbered later, the data's `section` must follow.
