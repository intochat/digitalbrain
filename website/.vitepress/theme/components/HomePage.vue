<script setup>
import { onMounted, onUnmounted } from 'vue'
import { withBase } from 'vitepress'

let observer

onMounted(() => {
  document.body.classList.add('brain-homepage')
  observer = new IntersectionObserver(
    entries => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          entry.target.classList.add('is-visible')
          observer.unobserve(entry.target)
        }
      }
    },
    { threshold: 0.12 }
  )
  document.querySelectorAll('.reveal').forEach(element => observer.observe(element))
})

onUnmounted(() => {
  document.body.classList.remove('brain-homepage')
  observer?.disconnect()
})

const evidence = [
  {
    name: 'Universal grain',
    symbol: 'NeuronGrain / INeuron',
    detail: 'One Orleans grain hosts every addressable capability.'
  },
  {
    name: 'Kind strategy',
    symbol: 'INeuronKind',
    detail: 'Registered kinds own deterministic domain behavior.'
  },
  {
    name: 'Typed client façade',
    symbol: 'INeuronContract / NeuronProxy',
    detail: 'Typed contracts translate to the universal invocation envelope.'
  },
  {
    name: 'Governed effects',
    symbol: 'Propose → decide → claim',
    detail: 'External mutations pass through the kernel effect gate.'
  }
]

const modules = ['Workspace', 'AI', 'Web', 'Connections', 'Google', 'Salesforce', 'Behaviors']
</script>

<template>
  <main class="brain-home">
    <section class="brain-hero">
      <div class="hero-grid" aria-hidden="true"></div>
      <div class="hero-glow hero-glow-one" aria-hidden="true"></div>
      <div class="hero-glow hero-glow-two" aria-hidden="true"></div>
      <svg class="hero-network" viewBox="0 0 1200 680" preserveAspectRatio="xMidYMid slice" aria-hidden="true">
        <g class="network-lines">
          <path d="M40 520 210 420 350 495 515 325 690 390 835 225 1010 300 1180 145" />
          <path d="M80 155 245 245 410 125 515 325 640 145 835 225 945 70" />
          <path d="M210 420 245 245 515 325 610 560 690 390 1010 300 1090 540" />
          <path d="M350 495 610 560 835 225 1090 540" />
        </g>
        <g class="network-nodes">
          <circle cx="40" cy="520" r="4" />
          <circle cx="80" cy="155" r="3" />
          <circle cx="210" cy="420" r="5" />
          <circle cx="245" cy="245" r="4" />
          <circle cx="350" cy="495" r="3" />
          <circle cx="410" cy="125" r="4" />
          <circle cx="515" cy="325" r="8" class="network-core" />
          <circle cx="610" cy="560" r="4" />
          <circle cx="640" cy="145" r="3" />
          <circle cx="690" cy="390" r="5" />
          <circle cx="835" cy="225" r="5" />
          <circle cx="945" cy="70" r="3" />
          <circle cx="1010" cy="300" r="4" />
          <circle cx="1090" cy="540" r="4" />
          <circle cx="1180" cy="145" r="3" />
        </g>
        <circle r="3" class="signal signal-one">
          <animateMotion dur="5s" repeatCount="indefinite" path="M40 520 L210 420 L350 495 L515 325 L690 390 L835 225 L1010 300 L1180 145" />
        </circle>
        <circle r="3" class="signal signal-two">
          <animateMotion dur="6s" repeatCount="indefinite" path="M80 155 L245 245 L515 325 L610 560 L690 390 L1010 300 L1090 540" />
        </circle>
      </svg>

      <div class="hero-content">
        <div class="eyebrow">Open-source capability operating system</div>
        <h1>Digital<span>Brain</span></h1>
        <p class="hero-headline">A small kernel for durable, addressable capabilities.</p>
        <p class="hero-copy">
          One address space, typed contracts, and an explicit effect gate for software,
          people, and agents working in the same system.
        </p>
        <div class="hero-actions">
          <a :href="withBase('/getting-started/first-call')" class="button button-primary">First MCP call</a>
          <a :href="withBase('/reference/status')" class="button button-secondary">View implementation status <span>→</span></a>
        </div>
      </div>

      <div class="hero-status">
        <span class="status-pulse"></span>
        <span>Local-first development stack</span>
        <span class="status-divider"></span>
        <span>.NET · Orleans · Aspire</span>
      </div>
    </section>

    <section class="home-section evidence-section">
      <div class="section-inner">
        <div class="section-kicker reveal">Implemented</div>
        <h2 class="reveal">What runs today</h2>
        <p class="section-lead reveal">
          These claims map to code in the repository. The status reference records the gaps without disguising targets as shipped behavior.
        </p>
        <div class="evidence-grid">
          <article v-for="item in evidence" :key="item.name" class="evidence-card reveal">
            <span class="evidence-state">Implemented</span>
            <h3>{{ item.name }}</h3>
            <code>{{ item.symbol }}</code>
            <p>{{ item.detail }}</p>
          </article>
        </div>
      </div>
    </section>

    <section class="home-section pipeline-section">
      <div class="section-inner system-layout">
        <div class="system-copy">
          <div class="section-kicker reveal">One execution path</div>
          <h2 class="reveal">Typed at the client.<br>Universal in the kernel.</h2>
          <p class="section-lead reveal">
            Module contracts stay discoverable while the kernel keeps one invocation, persistence, and policy path.
          </p>
          <a :href="withBase('/guide/programming-model')" class="text-link reveal">See the programming model <span>→</span></a>
        </div>
        <div class="pipeline-panel reveal" aria-label="Current invocation pipeline">
          <div class="pipeline-step"><strong>INeuronContract</strong><span>Module API</span></div>
          <span class="pipeline-arrow">→</span>
          <div class="pipeline-step"><strong>NeuronProxy</strong><span>Typed façade</span></div>
          <span class="pipeline-arrow">→</span>
          <div class="pipeline-step"><strong>INeuron.InvokeAsync</strong><span>Kernel envelope</span></div>
          <span class="pipeline-arrow">→</span>
          <div class="pipeline-step"><strong>INeuronKind</strong><span>Domain strategy</span></div>
        </div>
      </div>
    </section>

    <section class="home-section status-section">
      <div class="section-inner">
        <div class="section-kicker reveal">Evidence before aspiration</div>
        <h2 class="reveal">Know what each claim means.</h2>
        <div class="status-grid">
          <article class="status-card status-implemented reveal">
            <span>Implemented</span>
            <h3>Backed by code and tests</h3>
            <p>Safe to evaluate in the current repository.</p>
          </article>
          <article class="status-card status-target reveal">
            <span>Target</span>
            <h3>Accepted direction</h3>
            <p>Intended architecture that is not fully delivered yet.</p>
          </article>
          <article class="status-card status-decision reveal">
            <span>Decision</span>
            <h3>Still deliberately open</h3>
            <p>A boundary that needs evidence before it becomes contract.</p>
          </article>
        </div>
        <a :href="withBase('/reference/status')" class="text-link reveal">Read the complete implementation ledger <span>→</span></a>
      </div>
    </section>

    <section class="home-section module-section">
      <div class="section-inner">
        <div class="section-kicker reveal">Current composition</div>
        <h2 class="reveal">Modules already wired into the host.</h2>
        <p class="section-lead reveal">
          Today, modules use explicit host composition. Packaging, manifests, and community isolation remain future decisions.
        </p>
        <div class="module-catalog reveal">
          <span v-for="module in modules" :key="module" class="module-pill">{{ module }}</span>
        </div>
        <a :href="withBase('/build/first-module')" class="text-link reveal">Build a module against the current model <span>→</span></a>
      </div>
    </section>

    <section class="home-cta">
      <div class="cta-glow" aria-hidden="true"></div>
      <div class="section-inner reveal">
        <div class="section-kicker">Start from running code</div>
        <h2>Make the first call, then trace it to the kernel.</h2>
        <p>Run the Aspire topology, invoke a neuron through MCP, and add a tested kind without inventing a parallel runtime.</p>
        <div class="hero-actions">
          <a :href="withBase('/getting-started/first-call')" class="button button-primary">First MCP call</a>
          <a :href="withBase('/build/first-module')" class="button button-secondary">Build a module <span>→</span></a>
        </div>
      </div>
    </section>
  </main>
</template>
