<script setup>
const nodes = [
  { id: 'edge', label: 'MCP / UI', x: 80, y: 215, kind: 'experience' },
  { id: 'grain', label: 'NeuronGrain', x: 260, y: 215, kind: 'core' },
  { id: 'kind', label: 'INeuronKind', x: 440, y: 215, kind: 'kernel' },
  { id: 'workspace', label: 'Workspace', x: 560, y: 75, kind: 'module' },
  { id: 'google', label: 'Google', x: 560, y: 215, kind: 'module' },
  { id: 'effects', label: 'Effect gate', x: 560, y: 355, kind: 'module' }
]

const edges = [
  ['edge', 'grain'],
  ['grain', 'kind'],
  ['kind', 'workspace'],
  ['kind', 'google'],
  ['kind', 'effects']
]

const node = id => nodes.find(item => item.id === id)
</script>

<template>
  <div class="doc-neuron-graph">
    <svg viewBox="0 0 640 430" role="img" aria-label="Current DigitalBrain execution path">
      <defs>
        <filter id="doc-glow">
          <feGaussianBlur stdDeviation="5" result="blur" />
          <feMerge>
            <feMergeNode in="blur" />
            <feMergeNode in="SourceGraphic" />
          </feMerge>
        </filter>
      </defs>
      <g class="doc-edges">
        <line
          v-for="([from, to], index) in edges"
          :key="index"
          :x1="node(from).x"
          :y1="node(from).y"
          :x2="node(to).x"
          :y2="node(to).y"
        />
      </g>
      <g
        v-for="item in nodes"
        :key="item.id"
        class="doc-node"
        :class="`doc-node-${item.kind}`"
        :transform="`translate(${item.x} ${item.y})`"
      >
        <circle r="29" class="doc-node-halo" />
        <circle r="10" class="doc-node-core" filter="url(#doc-glow)" />
        <text y="48" text-anchor="middle">{{ item.label }}</text>
      </g>
      <circle r="3" class="doc-signal">
        <animateMotion dur="4s" repeatCount="indefinite" path="M80 215 L260 215 L440 215 L560 75" />
      </circle>
      <circle r="3" class="doc-signal doc-signal-alt">
        <animateMotion dur="5s" repeatCount="indefinite" path="M80 215 L260 215 L440 215 L560 355" />
      </circle>
    </svg>
  </div>
</template>
