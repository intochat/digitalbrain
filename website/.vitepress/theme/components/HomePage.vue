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

const layers = [
  { name: 'Experiences', detail: 'Workspace · MCP · behaviors', tone: 'violet' },
  { name: 'Modules', detail: 'AI · memory · Stripe · community', tone: 'blue' },
  { name: 'Kernel', detail: 'Identity · scheduling · policy · effects', tone: 'cyan' },
  { name: 'Runtime', detail: 'Orleans · Aspire · durable state', tone: 'slate' }
]
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
        <div class="eyebrow">Open-source neuron operating system</div>
        <h1>Digital<span>Brain</span></h1>
        <p class="hero-headline">An operating system built from neurons and synapses.</p>
        <p class="hero-copy">
          Everything addressable is a neuron. Typed connections let software,
          people, and agents grow one coherent brain.
        </p>
        <div class="hero-actions">
          <a :href="withBase('/guide/')" class="button button-primary">Start exploring</a>
          <a :href="withBase('/guide/architecture')" class="button button-secondary">Explore the architecture <span>→</span></a>
        </div>
      </div>

      <div class="hero-status">
        <span class="status-pulse"></span>
        <span>Kernel online</span>
        <span class="status-divider"></span>
        <span>.NET · Orleans · Aspire</span>
      </div>
    </section>

    <section class="home-section primitives">
      <div class="section-inner">
        <div class="section-kicker reveal">The model</div>
        <h2 class="reveal">Two primitives. One living system.</h2>
        <p class="section-lead reveal">
          DigitalBrain treats identity and connection as the foundation,
          then builds everything else as modules.
        </p>
        <div class="primitive-grid">
          <article class="primitive-card reveal">
            <div class="primitive-visual neuron-visual" aria-hidden="true">
              <span class="neuron-halo halo-three"></span>
              <span class="neuron-halo halo-two"></span>
              <span class="neuron-halo halo-one"></span>
              <span class="neuron-dot"></span>
            </div>
            <div class="primitive-number">01</div>
            <h3>Neurons</h3>
            <p>Durable, addressable capabilities with typed contracts and explicit ownership.</p>
            <a :href="withBase('/guide/neurons')">Understand neurons <span>→</span></a>
          </article>
          <article class="primitive-card reveal">
            <div class="primitive-visual synapse-visual" aria-hidden="true">
              <span class="synapse-node node-left"></span>
              <span class="synapse-line"></span>
              <span class="synapse-signal"></span>
              <span class="synapse-node node-right"></span>
            </div>
            <div class="primitive-number">02</div>
            <h3>Synapses</h3>
            <p>Typed facts and governed relationships that connect neurons without becoming a second command bus.</p>
            <a :href="withBase('/guide/synapses')">Understand synapses <span>→</span></a>
          </article>
        </div>
      </div>
    </section>

    <section class="home-section system-section">
      <div class="section-inner system-layout">
        <div class="system-copy">
          <div class="section-kicker reveal">The system</div>
          <h2 class="reveal">Small kernel.<br>Infinite surface.</h2>
          <p class="section-lead reveal">
            The kernel owns the invariants. Modules add product capability.
            Experiences remain replaceable views over the same brain.
          </p>
          <a :href="withBase('/guide/modules')" class="text-link reveal">Build a module <span>→</span></a>
        </div>
        <div class="layer-stack reveal">
          <div v-for="layer in layers" :key="layer.name" class="layer" :class="`layer-${layer.tone}`">
            <div>
              <strong>{{ layer.name }}</strong>
              <span>{{ layer.detail }}</span>
            </div>
            <span class="layer-light"></span>
          </div>
        </div>
      </div>
    </section>

    <section class="home-section module-section">
      <div class="section-inner">
        <div class="section-kicker reveal">Made to extend</div>
        <h2 class="reveal">A module ships a complete capability.</h2>
        <div class="module-flow reveal">
          <div class="module-node">
            <span>01</span>
            <strong>Contracts</strong>
            <small>Typed public surface</small>
          </div>
          <div class="module-connector"></div>
          <div class="module-node">
            <span>02</span>
            <strong>Runtime</strong>
            <small>Neuron implementation</small>
          </div>
          <div class="module-connector"></div>
          <div class="module-node">
            <span>03</span>
            <strong>Connector</strong>
            <small>External systems</small>
          </div>
          <div class="module-connector"></div>
          <div class="module-node">
            <span>04</span>
            <strong>UI</strong>
            <small>Native projections</small>
          </div>
          <div class="module-connector"></div>
          <div class="module-node">
            <span>05</span>
            <strong>Hosting</strong>
            <small>Aspire composition</small>
          </div>
        </div>
        <div class="example-strip reveal">
          <span class="example-label">Examples</span>
          <span>Memory</span>
          <span>Stripe</span>
          <span>Google</span>
          <span>Salesforce</span>
          <span>Community</span>
        </div>
      </div>
    </section>

    <section class="home-cta">
      <div class="cta-glow" aria-hidden="true"></div>
      <div class="section-inner reveal">
        <div class="section-kicker">Begin with the kernel</div>
        <h2>Build software that can grow.</h2>
        <p>Read the architecture, follow the decisions, and help shape the neuron ecosystem.</p>
        <div class="hero-actions">
          <a :href="withBase('/guide/')" class="button button-primary">Read the guide</a>
          <a :href="withBase('/contributing/')" class="button button-secondary">Contribute <span>→</span></a>
        </div>
      </div>
    </section>
  </main>
</template>
