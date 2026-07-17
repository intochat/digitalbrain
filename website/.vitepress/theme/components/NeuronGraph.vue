<script setup>
const nodes = [
  { id: 'identity', label: 'Identity', x: 110, y: 80, kind: 'kernel' },
  { id: 'kernel', label: 'Kernel', x: 300, y: 190, kind: 'core' },
  { id: 'memory', label: 'Memory', x: 500, y: 80, kind: 'module' },
  { id: 'stripe', label: 'Stripe', x: 540, y: 250, kind: 'module' },
  { id: 'workspace', label: 'Workspace', x: 280, y: 350, kind: 'experience' },
  { id: 'mcp', label: 'MCP', x: 70, y: 290, kind: 'experience' }
]

const edges = [
  ['identity', 'kernel'],
  ['kernel', 'memory'],
  ['kernel', 'stripe'],
  ['kernel', 'workspace'],
  ['kernel', 'mcp'],
  ['memory', 'workspace'],
  ['stripe', 'workspace']
]

const node = id => nodes.find(item => item.id === id)
</script>

<template>
  <div class="doc-neuron-graph">
    <svg viewBox="0 0 620 430" role="img" aria-label="DigitalBrain neuron architecture">
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
        <animateMotion dur="4s" repeatCount="indefinite" path="M110 80 L300 190 L500 80" />
      </circle>
      <circle r="3" class="doc-signal doc-signal-alt">
        <animateMotion dur="5s" repeatCount="indefinite" path="M70 290 L300 190 L540 250 L280 350" />
      </circle>
    </svg>
  </div>
</template>
