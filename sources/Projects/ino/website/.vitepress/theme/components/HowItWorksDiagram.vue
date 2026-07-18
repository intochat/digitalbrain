<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'

interface NeuronNode {
  label: string
  description: string
  cx: number
  cy: number
  r: number
}

interface SynapseEdge {
  from: string
  to: string
  verb: string
  description: string
  x1: number; y1: number
  x2: number; y2: number
}

interface BehaviorBadge {
  id: string
  label: string
  neurons: string[]
  verb: string
  x: number; y: number
  scenario: string
}

const neurons: Record<string, NeuronNode> = {
  assistant: {
    label: 'assistant',
    description: 'Orchestrating neuron. Receives user requests, selects the right neurons, and delegates work via synapses. The central coordinator of any multi-step task.',
    cx: 400, cy: 90, r: 32,
  },
  shell: {
    label: 'shell',
    description: 'Executes shell commands in a sandboxed workspace. Created at runtime via "ino create shell". Every command execution is a synapse fired to the timeline.',
    cx: 120, cy: 210, r: 24,
  },
  git: {
    label: 'git',
    description: 'Version control neuron. Commits, branches, diffs — all exposed as synapse verbs. Connected to shell for command execution and to roslyn for code-aware operations.',
    cx: 310, cy: 260, r: 24,
  },
  roslyn: {
    label: 'roslyn',
    description: 'Code analysis neuron powered by Microsoft.CodeAnalysis. Parses ASTs, extracts signatures, validates scripts. The brain behind code-aware behaviors.',
    cx: 500, cy: 260, r: 24,
  },
  dotnet: {
    label: 'dotnet',
    description: 'Build and test neuron. Runs dotnet build, dotnet test, dotnet format. Wired to roslyn for compilation and to shell for process execution.',
    cx: 680, cy: 210, r: 24,
  },
  nuget: {
    label: 'nuget',
    description: 'Package management neuron. Monitors nuget.org for updates, resolves dependencies, and applies package upgrades across the solution.',
    cx: 590, cy: 140, r: 20,
  },
}

const synapses: SynapseEdge[] = [
  {
    from: 'assistant', to: 'shell', verb: 'delegate',
    description: 'Assistant delegates a command execution task to the shell neuron. The synapse carries the command string as payload.',
    x1: 375, y1: 115, x2: 140, y2: 192,
  },
  {
    from: 'assistant', to: 'git', verb: 'commit',
    description: 'Assistant asks git to commit staged changes. The synapse verb "commit" maps directly to the git neuron\'s commit capability.',
    x1: 390, y1: 120, x2: 315, y2: 238,
  },
  {
    from: 'assistant', to: 'roslyn', verb: 'analyze',
    description: 'Assistant requests code analysis from roslyn. The synapse carries the file path or code snippet to analyze.',
    x1: 420, y1: 120, x2: 495, y2: 238,
  },
  {
    from: 'assistant', to: 'dotnet', verb: 'build',
    description: 'Assistant triggers a build via the dotnet neuron. Results flow back as a response synapse with build output.',
    x1: 430, y1: 110, x2: 660, y2: 195,
  },
  {
    from: 'shell', to: 'git', verb: 'exec',
    description: 'Shell executes a git command on behalf of the git neuron. Low-level process execution synapse.',
    x1: 142, y1: 225, x2: 290, y2: 252,
  },
  {
    from: 'roslyn', to: 'dotnet', verb: 'compile',
    description: 'Roslyn hands off a validated script to dotnet for compilation. The synapse carries the Roslyn compilation result.',
    x1: 522, y1: 250, x2: 658, y2: 222,
  },
  {
    from: 'dotnet', to: 'nuget', verb: 'resolve',
    description: 'Dotnet asks nuget to resolve package dependencies before a build. Returns the resolved dependency graph.',
    x1: 668, y1: 195, x2: 600, y2: 155,
  },
]

const behaviors: BehaviorBadge[] = [
  {
    id: 'code-review',
    label: 'code-review',
    neurons: ['assistant', 'roslyn', 'git'],
    verb: 'analyze',
    x: 60, y: 25,
    scenario: `Feature: Code review behavior

  Scenario: Review changes before commit
    Given neurons "assistant", "roslyn", and "git" exist
    And a synapse "analyze" connects assistant to roslyn
    And a synapse "commit" connects assistant to git
    When assistant fires "analyze" with the current diff
    Then roslyn returns analysis results
    And the timeline contains a SynapseFired event with verb "analyze"`,
  },
  {
    id: 'auto-build',
    label: 'auto-build',
    neurons: ['assistant', 'dotnet', 'roslyn'],
    verb: 'build',
    x: 540, y: 25,
    scenario: `Feature: Auto-build behavior

  Scenario: Build after code change
    Given neurons "assistant", "dotnet", and "roslyn" exist
    And a synapse "build" connects assistant to dotnet
    And a synapse "compile" connects roslyn to dotnet
    When assistant fires "build" with the solution path
    Then dotnet returns the build result
    And the timeline contains a SynapseFired event with verb "build"`,
  },
  {
    id: 'smart-commit',
    label: 'smart-commit',
    neurons: ['assistant', 'shell', 'git'],
    verb: 'commit',
    x: 280, y: 330,
    scenario: `Feature: Smart commit behavior

  Scenario: Commit with contextual message
    Given neurons "assistant", "shell", and "git" exist
    And a synapse "delegate" connects assistant to shell
    And a synapse "commit" connects assistant to git
    When assistant fires "commit" with payload "fix: typo in readme"
    Then git receives the synapse
    And the timeline contains a SynapseFired event with verb "commit"
    And the synapse decay is 100`,
  },
]

const wrapperRef = ref<HTMLElement | null>(null)
const visible = ref(false)
const hoveredNeuron = ref<string | null>(null)
const lockedNeuron = ref<string | null>(null)
const activeNeuron = computed(() => lockedNeuron.value ?? hoveredNeuron.value)
const hoveredSynapse = ref<number | null>(null)
const activeBehavior = ref<string | null>(null)

onMounted(() => {
  const obs = new IntersectionObserver(
    ([e]) => { if (e.isIntersecting) { visible.value = true; obs.disconnect() } },
    { threshold: 0.1 },
  )
  if (wrapperRef.value) obs.observe(wrapperRef.value)
})

function onNeuronEnter(id: string) { if (!lockedNeuron.value) hoveredNeuron.value = id }
function onNeuronLeave() { if (!lockedNeuron.value) hoveredNeuron.value = null }
function onNeuronClick(id: string) {
  activeBehavior.value = null
  if (lockedNeuron.value === id) { lockedNeuron.value = null; hoveredNeuron.value = null }
  else { lockedNeuron.value = id; hoveredNeuron.value = id }
}

function onSynapseEnter(i: number) { hoveredSynapse.value = i }
function onSynapseLeave() { hoveredSynapse.value = null }

function onBehaviorClick(id: string) {
  lockedNeuron.value = null
  hoveredNeuron.value = null
  activeBehavior.value = activeBehavior.value === id ? null : id
}

function neuronClass(id: string): string {
  const a = activeNeuron.value
  const ab = activeBehavior.value
  if (ab) {
    const b = behaviors.find(x => x.id === ab)
    return b?.neurons.includes(id) ? 'neuron neuron-active' : 'neuron neuron-dimmed'
  }
  if (!a) return 'neuron'
  if (a === id) return 'neuron neuron-active'
  const connected = synapses.some(s => (s.from === a && s.to === id) || (s.to === a && s.from === id))
  return connected ? 'neuron' : 'neuron neuron-dimmed'
}

function synapseClass(s: SynapseEdge): string {
  const a = activeNeuron.value
  const ab = activeBehavior.value
  if (ab) {
    const b = behaviors.find(x => x.id === ab)
    if (!b) return 'synapse-line'
    return (b.neurons.includes(s.from) && b.neurons.includes(s.to)) ? 'synapse-line synapse-active' : 'synapse-line synapse-dimmed'
  }
  if (!a) return 'synapse-line'
  if (s.from === a) return 'synapse-line synapse-active'
  if (s.to === a) return 'synapse-line synapse-incoming'
  return 'synapse-line synapse-dimmed'
}

function markerEnd(s: SynapseEdge): string {
  const a = activeNeuron.value
  const ab = activeBehavior.value
  if (ab) {
    const b = behaviors.find(x => x.id === ab)
    return (b?.neurons.includes(s.from) && b?.neurons.includes(s.to)) ? 'url(#hiw-ah-brand)' : 'url(#hiw-ah)'
  }
  return (a && s.from === a) ? 'url(#hiw-ah-brand)' : 'url(#hiw-ah)'
}

const descriptionText = computed(() => {
  if (activeBehavior.value) return null
  const a = activeNeuron.value
  if (a && neurons[a]) return { title: neurons[a].label, text: neurons[a].description }
  if (hoveredSynapse.value !== null) {
    const s = synapses[hoveredSynapse.value]
    return { title: `${s.from} → ${s.to} (${s.verb})`, text: s.description }
  }
  return null
})

const activeBehaviorData = computed(() => {
  if (!activeBehavior.value) return null
  return behaviors.find(b => b.id === activeBehavior.value) ?? null
})
</script>

<template>
  <div ref="wrapperRef" class="hiw-wrapper" :class="{ visible }">
    <div class="hiw-canvas">
      <svg class="hiw-svg" viewBox="0 0 800 370" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <marker id="hiw-ah" viewBox="0 0 10 7" refX="10" refY="3.5" markerWidth="7" markerHeight="5" orient="auto">
            <path d="M0,0 L10,3.5 L0,7Z" class="mk" />
          </marker>
          <marker id="hiw-ah-brand" viewBox="0 0 10 7" refX="10" refY="3.5" markerWidth="7" markerHeight="5" orient="auto">
            <path d="M0,0 L10,3.5 L0,7Z" class="mk-b" />
          </marker>
        </defs>

        <!-- Signal pulses -->
        <g class="row row-pulses">
          <circle v-for="(s, i) in synapses" :key="'p'+i" r="2.5" class="pulse">
            <animateMotion
              :dur="(1.6 + i * 0.3).toFixed(1) + 's'"
              :begin="(i * 0.5).toFixed(1) + 's'"
              repeatCount="indefinite"
              :path="`M${s.x1},${s.y1} L${s.x2},${s.y2}`"
            />
          </circle>
        </g>

        <!-- Synapse lines -->
        <g class="row row-synapses">
          <g v-for="(s, i) in synapses" :key="'s'+i"
             :class="synapseClass(s)"
             @mouseenter="onSynapseEnter(i)"
             @mouseleave="onSynapseLeave"
          >
            <line :x1="s.x1" :y1="s.y1" :x2="s.x2" :y2="s.y2" class="sl" :marker-end="markerEnd(s)" />
            <text
              :x="(s.x1 + s.x2) / 2"
              :y="(s.y1 + s.y2) / 2 - 6"
              class="verb-label"
            >{{ s.verb }}</text>
          </g>
        </g>

        <!-- Neuron nodes -->
        <g class="row row-neurons">
          <g v-for="(n, id) in neurons" :key="id"
             :class="neuronClass(id)"
             @mouseenter="onNeuronEnter(id)"
             @mouseleave="onNeuronLeave"
             @click="onNeuronClick(id)"
          >
            <circle :cx="n.cx" :cy="n.cy" :r="n.r" class="neuron-circle" />
            <text :x="n.cx" :y="n.cy + 1" class="neuron-label">{{ n.label }}</text>
          </g>
        </g>

        <!-- Behavior badges -->
        <g class="row row-behaviors">
          <g v-for="b in behaviors" :key="b.id"
             :class="['behavior', activeBehavior === b.id ? 'behavior-active' : '']"
             @click="onBehaviorClick(b.id)"
             style="cursor:pointer;"
          >
            <rect :x="b.x" :y="b.y" width="130" height="26" rx="6" class="behavior-bg" />
            <text :x="b.x + 65" :y="b.y + 17" class="behavior-label">✓ {{ b.label }}</text>
          </g>
        </g>
      </svg>
    </div>

    <!-- Description panel (neuron/synapse hover) -->
    <Transition name="fade" mode="out-in">
      <div v-if="descriptionText" :key="descriptionText.title" class="dp">
        <div class="dp-head">
          <h3>{{ descriptionText.title }}</h3>
          <span v-if="lockedNeuron" class="dp-hint">click to unlock</span>
        </div>
        <p>{{ descriptionText.text }}</p>
      </div>

      <!-- BDD panel (behavior badge click) -->
      <div v-else-if="activeBehaviorData" :key="activeBehaviorData.id" class="dp dp-bdd">
        <div class="dp-head">
          <h3>{{ activeBehaviorData.label }}</h3>
          <span class="dp-hint">click badge to close</span>
        </div>
        <p class="dp-neurons">
          Neurons: <span v-for="(n, i) in activeBehaviorData.neurons" :key="n">
            <code>{{ n }}</code><span v-if="i < activeBehaviorData.neurons.length - 1">, </span>
          </span>
        </p>
        <pre class="dp-gherkin"><code>{{ activeBehaviorData.scenario }}</code></pre>
      </div>

      <!-- Default hint -->
      <div v-else key="default" class="dp dp-empty">
        <p>Hover any neuron or synapse to explore · click a green behavior badge to see its BDD scenario</p>
      </div>
    </Transition>
  </div>
</template>

<style scoped>
/* ── Layout ── */
.hiw-wrapper { max-width: 860px; margin: 24px auto; overflow-x: auto; }
.hiw-canvas {
  background: var(--vp-c-bg-alt);
  border: 1px solid var(--vp-c-divider);
  border-radius: 14px;
  padding: 12px 8px;
}
.hiw-svg { width: 100%; min-width: 600px; height: auto; display: block; }

/* ── Entrance ── */
.row { opacity: 0; transform: translateY(14px); }
.visible .row { animation: hiw-enter 0.5s ease-out forwards; }
.visible .row-neurons    { animation-delay: 0.1s; }
.visible .row-synapses   { animation-delay: 0.25s; }
.visible .row-pulses     { animation-delay: 0.4s; }
.visible .row-behaviors  { animation-delay: 0.5s; }
@keyframes hiw-enter {
  from { opacity: 0; transform: translateY(14px); }
  to   { opacity: 1; transform: translateY(0); }
}

/* ── Pulses ── */
.pulse { fill: var(--vp-c-brand-1); opacity: 0.35; }

/* ── Synapse lines ── */
.sl {
  stroke: var(--vp-c-divider);
  stroke-width: 1;
  stroke-dasharray: 5 4;
  fill: none;
  transition: stroke 0.25s, stroke-width 0.25s, opacity 0.25s;
}
.synapse-line { cursor: pointer; transition: opacity 0.3s; }
.synapse-active .sl { stroke: var(--vp-c-brand-1); stroke-width: 1.5; }
.synapse-incoming .sl { opacity: 0.4; }
.synapse-dimmed { opacity: 0.12; }
.verb-label {
  fill: var(--vp-c-brand-2);
  font-family: var(--vp-font-family-base);
  font-size: 9px;
  font-weight: 500;
  text-anchor: middle;
  pointer-events: none;
  opacity: 0.6;
}
.synapse-active .verb-label { opacity: 1; }
.synapse-dimmed .verb-label { opacity: 0.15; }

/* ── Markers ── */
.mk   { fill: var(--vp-c-divider); }
.mk-b { fill: var(--vp-c-brand-1); }

/* ── Neurons ── */
.neuron { cursor: pointer; transition: opacity 0.3s; }
.neuron-circle {
  fill: var(--vp-c-bg-soft);
  stroke: var(--vp-c-brand-1);
  stroke-width: 1.2;
  transition: stroke 0.2s, filter 0.3s, stroke-width 0.2s;
}
.neuron:hover .neuron-circle { stroke-width: 1.8; }
.neuron-active .neuron-circle {
  stroke-width: 2;
  animation: hiw-glow 2.4s ease-in-out infinite;
}
.neuron-dimmed { opacity: 0.15; }
.neuron-label {
  fill: var(--vp-c-brand-1);
  font-family: var(--vp-font-family-base);
  font-size: 11px;
  font-weight: 700;
  text-anchor: middle;
  dominant-baseline: middle;
  pointer-events: none;
}
@keyframes hiw-glow {
  0%, 100% { filter: drop-shadow(0 0 3px var(--vp-c-brand-soft)); }
  50%      { filter: drop-shadow(0 0 10px var(--vp-c-brand-soft)); }
}

/* ── Behavior badges ── */
.behavior-bg {
  fill: rgba(110, 231, 183, 0.06);
  stroke: rgba(110, 231, 183, 0.2);
  stroke-width: 0.8;
  transition: stroke 0.2s, fill 0.2s;
}
.behavior:hover .behavior-bg {
  fill: rgba(110, 231, 183, 0.12);
  stroke: rgba(110, 231, 183, 0.4);
}
.behavior-active .behavior-bg {
  fill: rgba(110, 231, 183, 0.15);
  stroke: rgba(110, 231, 183, 0.6);
}
.behavior-label {
  fill: #6ee7b7;
  font-family: var(--vp-font-family-base);
  font-size: 10.5px;
  font-weight: 600;
  text-anchor: middle;
  pointer-events: none;
}

/* ── Description panel ── */
.dp {
  margin-top: 14px;
  padding: 14px 18px;
  border-radius: 10px;
  border: 1px solid var(--vp-c-divider);
  background: var(--vp-c-bg-soft);
  min-height: 56px;
  transition: border-color 0.3s, box-shadow 0.3s;
}
.dp-bdd {
  border-color: rgba(110, 231, 183, 0.3);
  box-shadow: 0 0 20px -6px rgba(110, 231, 183, 0.15);
}
.dp-head { display: flex; justify-content: space-between; align-items: baseline; }
.dp-head h3 { margin: 0 0 4px; font-size: 15px; font-weight: 600; color: var(--vp-c-text-1); }
.dp p { margin: 0; font-size: 14px; line-height: 1.55; color: var(--vp-c-text-2); }
.dp-hint { font-size: 11px; color: var(--vp-c-text-3); white-space: nowrap; }
.dp-empty { text-align: center; }
.dp-empty p { color: var(--vp-c-text-3) !important; padding: 6px 0; }
.dp-neurons { margin-bottom: 10px !important; }
.dp-neurons code {
  font-size: 12px;
  padding: 1px 6px;
  border-radius: 4px;
  background: var(--vp-c-bg-alt);
  color: var(--vp-c-brand-1);
}
.dp-gherkin {
  margin: 0;
  padding: 12px 16px;
  border-radius: 8px;
  background: var(--vp-c-bg-alt);
  border: 1px solid var(--vp-c-divider);
  overflow-x: auto;
}
.dp-gherkin code {
  font-size: 12.5px;
  line-height: 1.7;
  color: var(--vp-c-text-2);
  white-space: pre;
}

/* ── Transition ── */
.fade-enter-active { transition: opacity 0.18s, transform 0.18s; }
.fade-leave-active { transition: opacity 0.1s; }
.fade-enter-from   { opacity: 0; transform: translateY(4px); }
.fade-leave-to     { opacity: 0; }
</style>
