<script setup>
import { onMounted, onUnmounted, ref } from 'vue'
import { withBase } from 'vitepress'

let observer = null

// ── Genesis growth animation ────────────────────────────────────
// 9 frames cycling every 12s. Each frame reveals more nodes + edges
// starting from just Creator → ino, ending in a dense neural mesh.
const genesisFrame = ref(0)
let genesisTimer = null

const GENESIS_FRAMES = 9
const GENESIS_STEP_MS = 1333 // 12s / 9 frames

onMounted(() => {
  document.body.classList.add('ino-homepage')

  observer = new IntersectionObserver(
    (entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) {
          entry.target.classList.add('in-view')
          observer.unobserve(entry.target)
        }
      })
    },
    { threshold: 0.12 }
  )

  document.querySelectorAll('.reveal').forEach((el) => observer.observe(el))

  genesisTimer = setInterval(() => {
    genesisFrame.value = (genesisFrame.value + 1) % GENESIS_FRAMES
  }, GENESIS_STEP_MS)
})

onUnmounted(() => {
  document.body.classList.remove('ino-homepage')
  if (observer) {
    observer.disconnect()
    observer = null
  }
  if (genesisTimer) {
    clearInterval(genesisTimer)
    genesisTimer = null
  }
})

function scrollToExplore() {
  const target = document.querySelector('.ino-primitives')
  if (target) target.scrollIntoView({ behavior: 'smooth' })
}

// Genesis node/edge registry. Each entry carries the frame it first appears in.
// Frame 0: Creator + ino only.
// Each subsequent frame cascades more nodes outward.
const genesisNodes = [
  { id: 'creator', label: 'you',       x: 180, y: 180, r: 6,   kind: 'creator', frame: 0 },
  { id: 'ino',     label: 'ino',       x: 330, y: 180, r: 11,  kind: 'ino',     frame: 0 },

  { id: 'gauth',   label: 'GoogleAuth', x: 470, y: 180, r: 5,  kind: 'domain',  frame: 1 },

  { id: 'gmail',   label: 'Gmail',      x: 560, y: 110, r: 4,  kind: 'app',     frame: 2 },
  { id: 'gcal',    label: 'Calendar',   x: 580, y: 180, r: 4,  kind: 'app',     frame: 2 },
  { id: 'gdrive',  label: 'Drive',      x: 560, y: 250, r: 4,  kind: 'app',     frame: 2 },

  { id: 'uber',    label: 'Uber',       x: 260, y: 80,  r: 4,  kind: 'app',     frame: 3 },
  { id: 'spot',    label: 'Spotify',    x: 420, y: 70,  r: 4,  kind: 'app',     frame: 3 },
  { id: 'git',     label: 'GitHub',     x: 210, y: 280, r: 4,  kind: 'app',     frame: 3 },

  { id: 'notion',  label: 'Notion',     x: 370, y: 300, r: 4,  kind: 'app',     frame: 4 },
  { id: 'figma',   label: 'Figma',      x: 125, y: 120, r: 4,  kind: 'app',     frame: 4 },
  { id: 'slack',   label: 'Slack',      x: 310, y: 40,  r: 4,  kind: 'app',     frame: 4 },

  { id: 'stripe',  label: 'Stripe',     x: 90,  y: 220, r: 3.5, kind: 'app',    frame: 5 },
  { id: 'maps',    label: 'Maps',       x: 140, y: 310, r: 3.5, kind: 'app',    frame: 5 },
  { id: 'booking', label: 'Booking',    x: 495, y: 310, r: 3.5, kind: 'app',    frame: 5 },

  { id: 'notes',   label: 'Notes',      x: 430, y: 270, r: 3,   kind: 'app',    frame: 6 },
  { id: 'tg',      label: 'Telegram',   x: 270, y: 245, r: 3,   kind: 'app',    frame: 6 },
  { id: 'px',      label: 'Photos',     x: 230, y: 150, r: 3,   kind: 'app',    frame: 6 },

  { id: 'linear',  label: 'Linear',     x: 380, y: 130, r: 3,   kind: 'app',    frame: 7 },
  { id: 'disc',    label: 'Discord',    x: 155, y: 230, r: 3,   kind: 'app',    frame: 7 },
  { id: 'strava',  label: 'Strava',     x: 520, y: 115, r: 3,   kind: 'app',    frame: 7 },

  { id: 'home',    label: 'Home',       x: 300, y: 320, r: 3,   kind: 'app',    frame: 8 },
  { id: 'weather', label: 'Weather',    x: 480, y: 245, r: 3,   kind: 'app',    frame: 8 },
]

const genesisEdges = [
  { from: 'creator', to: 'ino',    frame: 0, primary: true  },

  { from: 'ino',     to: 'gauth',  frame: 1 },

  { from: 'gauth',   to: 'gmail',  frame: 2 },
  { from: 'gauth',   to: 'gcal',   frame: 2 },
  { from: 'gauth',   to: 'gdrive', frame: 2 },

  { from: 'ino',     to: 'uber',   frame: 3 },
  { from: 'ino',     to: 'spot',   frame: 3 },
  { from: 'ino',     to: 'git',    frame: 3 },

  { from: 'ino',     to: 'notion', frame: 4 },
  { from: 'ino',     to: 'figma',  frame: 4 },
  { from: 'ino',     to: 'slack',  frame: 4 },

  { from: 'ino',     to: 'stripe', frame: 5 },
  { from: 'ino',     to: 'maps',   frame: 5 },
  { from: 'ino',     to: 'booking',frame: 5 },

  { from: 'ino',     to: 'notes',  frame: 6 },
  { from: 'ino',     to: 'tg',     frame: 6 },
  { from: 'ino',     to: 'px',     frame: 6 },

  { from: 'spot',    to: 'linear', frame: 7 },
  { from: 'git',     to: 'disc',   frame: 7 },
  { from: 'gcal',    to: 'strava', frame: 7 },

  { from: 'ino',     to: 'home',   frame: 8 },
  { from: 'gdrive',  to: 'weather',frame: 8 },
  { from: 'notion',  to: 'linear', frame: 8 },
  { from: 'slack',   to: 'tg',     frame: 8 },
  { from: 'figma',   to: 'notion', frame: 8 },
  { from: 'maps',    to: 'booking',frame: 8 },
]

function nodeById(id) {
  return genesisNodes.find(n => n.id === id)
}
</script>

<template>
  <div class="ino-home">

    <!-- ═══════ HERO ═══════ -->
    <section class="ino-hero">
      <div class="ino-hero-mesh" aria-hidden="true">
        <svg viewBox="0 0 1000 600" preserveAspectRatio="xMidYMid slice">
          <!-- connections -->
          <g stroke="rgba(255,255,255,0.05)" stroke-width="0.5" fill="none">
            <line x1="100" y1="100" x2="250" y2="180"/>
            <line x1="250" y1="180" x2="420" y2="80"/>
            <line x1="420" y1="80" x2="580" y2="150"/>
            <line x1="580" y1="150" x2="750" y2="70"/>
            <line x1="750" y1="70" x2="900" y2="160"/>
            <line x1="100" y1="100" x2="80" y2="350"/>
            <line x1="250" y1="180" x2="280" y2="400"/>
            <line x1="250" y1="180" x2="480" y2="340"/>
            <line x1="420" y1="80" x2="480" y2="340"/>
            <line x1="580" y1="150" x2="680" y2="390"/>
            <line x1="750" y1="70" x2="850" y2="320"/>
            <line x1="900" y1="160" x2="850" y2="320"/>
            <line x1="900" y1="160" x2="960" y2="420"/>
            <line x1="80" y1="350" x2="280" y2="400"/>
            <line x1="280" y1="400" x2="480" y2="340"/>
            <line x1="480" y1="340" x2="680" y2="390"/>
            <line x1="680" y1="390" x2="850" y2="320"/>
            <line x1="850" y1="320" x2="960" y2="420"/>
            <line x1="80" y1="350" x2="180" y2="540"/>
            <line x1="280" y1="400" x2="180" y2="540"/>
            <line x1="280" y1="400" x2="400" y2="520"/>
            <line x1="480" y1="340" x2="400" y2="520"/>
            <line x1="680" y1="390" x2="620" y2="550"/>
            <line x1="680" y1="390" x2="820" y2="500"/>
            <line x1="850" y1="320" x2="820" y2="500"/>
            <line x1="960" y1="420" x2="820" y2="500"/>
            <line x1="180" y1="540" x2="400" y2="520"/>
            <line x1="400" y1="520" x2="620" y2="550"/>
            <line x1="620" y1="550" x2="820" y2="500"/>
          </g>
          <!-- nodes -->
          <g>
            <circle cx="100" cy="100" r="2" fill="rgba(255,255,255,0.18)" class="mesh-node n1"/>
            <circle cx="250" cy="180" r="2.5" fill="rgba(255,255,255,0.22)" class="mesh-node n2"/>
            <circle cx="420" cy="80" r="2" fill="rgba(255,255,255,0.15)" class="mesh-node n3"/>
            <circle cx="580" cy="150" r="3" fill="rgba(255,255,255,0.25)" class="mesh-node n4"/>
            <circle cx="750" cy="70" r="2" fill="rgba(255,255,255,0.15)" class="mesh-node n5"/>
            <circle cx="900" cy="160" r="2.5" fill="rgba(255,255,255,0.2)" class="mesh-node n6"/>
            <circle cx="80" cy="350" r="2" fill="rgba(255,255,255,0.15)" class="mesh-node n7"/>
            <circle cx="280" cy="400" r="2.5" fill="rgba(255,255,255,0.22)" class="mesh-node n8"/>
            <circle cx="480" cy="340" r="3" fill="rgba(255,255,255,0.25)" class="mesh-node n9"/>
            <circle cx="680" cy="390" r="2" fill="rgba(255,255,255,0.18)" class="mesh-node n10"/>
            <circle cx="850" cy="320" r="2.5" fill="rgba(255,255,255,0.22)" class="mesh-node n11"/>
            <circle cx="960" cy="420" r="2" fill="rgba(255,255,255,0.15)" class="mesh-node n12"/>
            <circle cx="180" cy="540" r="2" fill="rgba(255,255,255,0.15)" class="mesh-node n13"/>
            <circle cx="400" cy="520" r="2.5" fill="rgba(255,255,255,0.2)" class="mesh-node n14"/>
            <circle cx="620" cy="550" r="2" fill="rgba(255,255,255,0.15)" class="mesh-node n15"/>
            <circle cx="820" cy="500" r="2.5" fill="rgba(255,255,255,0.2)" class="mesh-node n16"/>
          </g>
          <!-- signal pulses traveling along connections -->
          <circle r="1.5" fill="#818cf8" opacity="0.6">
            <animateMotion dur="3s" repeatCount="indefinite" path="M100,100 L250,180" begin="0s"/>
          </circle>
          <circle r="1.5" fill="#a5b4fc" opacity="0.5">
            <animateMotion dur="4s" repeatCount="indefinite" path="M420,80 L580,150" begin="1.2s"/>
          </circle>
          <circle r="1.5" fill="#818cf8" opacity="0.6">
            <animateMotion dur="3.5s" repeatCount="indefinite" path="M280,400 L480,340" begin="0.6s"/>
          </circle>
          <circle r="1.5" fill="#a5b4fc" opacity="0.5">
            <animateMotion dur="4.5s" repeatCount="indefinite" path="M680,390 L850,320" begin="2s"/>
          </circle>
          <circle r="1.5" fill="#818cf8" opacity="0.4">
            <animateMotion dur="5s" repeatCount="indefinite" path="M400,520 L620,550" begin="1.5s"/>
          </circle>
        </svg>
      </div>

      <div class="ino-hero-glow" aria-hidden="true"></div>

      <div class="ino-hero-content">
        <h1 class="ino-hero-brand">ino</h1>
        <p class="ino-hero-headline">A new kind of operating system.</p>
        <p class="ino-hero-sub">Built from neurons and synapses.<br>Software that grows with you.</p>
        <div class="ino-hero-ctas">
          <a href="#" class="ino-cta-primary" @click.prevent="scrollToExplore">Explore INO</a>
          <a :href="withBase('/guide/how-it-works')" class="ino-cta-secondary">How it works →</a>
        </div>
      </div>

      <div class="ino-hero-scroll" aria-hidden="true">
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <path d="M7 10l5 5 5-5"/>
        </svg>
      </div>
    </section>

    <!-- ═══════ GENESIS GROWTH ═══════ -->
    <section class="ino-genesis">
      <div class="ino-inner">
        <div class="ino-label reveal">Genesis</div>
        <h2 class="ino-heading reveal">Two dots become a brain.</h2>
        <p class="ino-body reveal">
          It starts with just you and ino. Every new service grows a new neuron.<br>
          Every connection becomes a synapse. Watch it assemble itself.
        </p>

        <div class="ino-genesis-visual reveal" aria-hidden="true">
          <svg viewBox="0 0 640 360" class="ino-genesis-svg">
            <defs>
              <filter id="genesis-blur-sm"><feGaussianBlur stdDeviation="2.5"/></filter>
              <filter id="genesis-blur-md"><feGaussianBlur stdDeviation="4"/></filter>
              <radialGradient id="genesis-ino-grad" cx="50%" cy="50%" r="50%">
                <stop offset="0%"  stop-color="#c7d2fe" stop-opacity="1"/>
                <stop offset="60%" stop-color="#818cf8" stop-opacity="0.85"/>
                <stop offset="100%" stop-color="#4338ca" stop-opacity="0"/>
              </radialGradient>
              <radialGradient id="genesis-creator-grad" cx="50%" cy="50%" r="50%">
                <stop offset="0%"  stop-color="#fde68a" stop-opacity="1"/>
                <stop offset="60%" stop-color="#f59e0b" stop-opacity="0.85"/>
                <stop offset="100%" stop-color="#b45309" stop-opacity="0"/>
              </radialGradient>
            </defs>

            <!-- edges -->
            <g class="genesis-edges">
              <line
                v-for="(e, i) in genesisEdges"
                :key="'ge-' + i"
                :x1="nodeById(e.from).x"
                :y1="nodeById(e.from).y"
                :x2="nodeById(e.to).x"
                :y2="nodeById(e.to).y"
                class="genesis-edge"
                :class="[
                  e.primary ? 'genesis-edge-primary' : '',
                  genesisFrame >= e.frame ? 'visible' : ''
                ]"
              />
            </g>

            <!-- halos for ino + creator -->
            <circle cx="180" cy="180" r="20" fill="url(#genesis-creator-grad)" filter="url(#genesis-blur-md)" class="genesis-halo visible" opacity="0.4"/>
            <circle cx="330" cy="180" r="28" fill="url(#genesis-ino-grad)" filter="url(#genesis-blur-md)" class="genesis-halo visible" opacity="0.5"/>

            <!-- nodes -->
            <g class="genesis-nodes">
              <g v-for="n in genesisNodes" :key="n.id"
                 class="genesis-node-group"
                 :class="[
                   'kind-' + n.kind,
                   genesisFrame >= n.frame ? 'visible' : ''
                 ]">
                <circle
                  :cx="n.x" :cy="n.y"
                  :r="n.r"
                  class="genesis-node"
                />
                <text
                  :x="n.x" :y="n.y - n.r - 5"
                  text-anchor="middle"
                  class="genesis-node-label"
                >
                  {{ n.label }}
                </text>
              </g>
            </g>

            <!-- signal pulses — only on creator→ino and ino→gauth once they're present -->
            <circle r="1.8" fill="#fde68a" opacity="0.7" :class="genesisFrame >= 0 ? 'genesis-pulse-on' : 'genesis-pulse-off'">
              <animateMotion dur="2.4s" repeatCount="indefinite" path="M180,180 L330,180"/>
            </circle>
            <circle r="1.8" fill="#a5b4fc" opacity="0.7" :class="genesisFrame >= 1 ? 'genesis-pulse-on' : 'genesis-pulse-off'">
              <animateMotion dur="2.8s" repeatCount="indefinite" path="M330,180 L470,180" begin="0.4s"/>
            </circle>
            <circle r="1.6" fill="#a5b4fc" opacity="0.55" :class="genesisFrame >= 2 ? 'genesis-pulse-on' : 'genesis-pulse-off'">
              <animateMotion dur="3.2s" repeatCount="indefinite" path="M470,180 L560,110" begin="0.8s"/>
            </circle>
            <circle r="1.6" fill="#a5b4fc" opacity="0.55" :class="genesisFrame >= 3 ? 'genesis-pulse-on' : 'genesis-pulse-off'">
              <animateMotion dur="3s" repeatCount="indefinite" path="M330,180 L420,70" begin="0.2s"/>
            </circle>
          </svg>
        </div>

        <!-- frame counter / caption -->
        <div class="ino-genesis-caption reveal">
          <span class="ino-genesis-dots">
            <span
              v-for="i in 9"
              :key="'fd-' + i"
              class="ino-genesis-dot"
              :class="{ active: genesisFrame === (i - 1) }"
            ></span>
          </span>
          <span class="ino-genesis-step">
            {{
              genesisFrame === 0 ? 'You and ino.' :
              genesisFrame === 1 ? 'Auth into Google.' :
              genesisFrame === 2 ? 'Three apps appear.' :
              genesisFrame === 3 ? 'Uber, Spotify, GitHub wire in.' :
              genesisFrame === 4 ? 'Creative and work apps join.' :
              genesisFrame === 5 ? 'Finance, maps, travel.' :
              genesisFrame === 6 ? 'Notes, chats, photos.' :
              genesisFrame === 7 ? 'Neurons form their own synapses.' :
              'A brain.'
            }}
          </span>
        </div>
      </div>
    </section>

    <!-- ═══════ TWO PRIMITIVES ═══════ -->
    <section class="ino-primitives">
      <div class="ino-inner">
        <h2 class="ino-heading reveal">Two primitives. That's all.</h2>
        <div class="ino-primitives-grid">
          <div class="ino-card reveal">
            <div class="ino-card-icon" aria-hidden="true">
              <svg width="48" height="48" viewBox="0 0 48 48" fill="none">
                <circle cx="24" cy="24" r="6" fill="#818cf8"/>
                <circle cx="24" cy="24" r="12" stroke="#818cf8" stroke-width="0.5" opacity="0.4"/>
                <circle cx="24" cy="24" r="20" stroke="#818cf8" stroke-width="0.3" opacity="0.2"/>
                <line x1="24" y1="4" x2="24" y2="12" stroke="#818cf8" stroke-width="0.5" opacity="0.3"/>
                <line x1="24" y1="36" x2="24" y2="44" stroke="#818cf8" stroke-width="0.5" opacity="0.3"/>
                <line x1="4" y1="24" x2="12" y2="24" stroke="#818cf8" stroke-width="0.5" opacity="0.3"/>
                <line x1="36" y1="24" x2="44" y2="24" stroke="#818cf8" stroke-width="0.5" opacity="0.3"/>
                <line x1="9.86" y1="9.86" x2="14.93" y2="14.93" stroke="#818cf8" stroke-width="0.4" opacity="0.2"/>
                <line x1="33.07" y1="33.07" x2="38.14" y2="38.14" stroke="#818cf8" stroke-width="0.4" opacity="0.2"/>
              </svg>
            </div>
            <h3>Neurons</h3>
            <p>Small, specialized intelligence units.<br>Each one an expert at a single thing.</p>
          </div>
          <div class="ino-card reveal delay-1">
            <div class="ino-card-icon" aria-hidden="true">
              <svg width="48" height="48" viewBox="0 0 48 48" fill="none">
                <circle cx="10" cy="24" r="4" fill="#a5b4fc"/>
                <circle cx="38" cy="24" r="4" fill="#a5b4fc"/>
                <path d="M14 24 C20 16, 28 16, 34 24" stroke="#a5b4fc" stroke-width="0.8" fill="none" opacity="0.5"/>
                <path d="M14 24 C20 32, 28 32, 34 24" stroke="#a5b4fc" stroke-width="0.8" fill="none" opacity="0.3"/>
                <circle cx="24" cy="19.5" r="1.2" fill="#a5b4fc" opacity="0.6">
                  <animateMotion dur="2.5s" repeatCount="indefinite" path="M-10,4.5 C-4,-3.5,4,-3.5,10,4.5"/>
                </circle>
              </svg>
            </div>
            <h3>Synapses</h3>
            <p>Event-driven connections that carry signal,<br>form memory, and become behavior.</p>
          </div>
        </div>
      </div>
    </section>

    <!-- ═══════ GROWS ═══════ -->
    <section class="ino-grows">
      <div class="ino-inner">
        <h2 class="ino-heading reveal">It grows as you use it.</h2>
        <p class="ino-body reveal">
          Every interaction creates new connections. New neurons emerge.<br>
          The system doesn't just respond — it evolves.
        </p>
        <div class="ino-grows-visual reveal" aria-hidden="true">
          <svg viewBox="0 0 260 100" class="ino-grows-svg">
            <defs>
              <filter id="grows-blur-sm"><feGaussianBlur stdDeviation="3"/></filter>
              <filter id="grows-blur-md"><feGaussianBlur stdDeviation="4"/></filter>
              <filter id="grows-blur-lg"><feGaussianBlur stdDeviation="5"/></filter>
            </defs>

            <!-- connection arcs between spheres -->
            <path d="M55,75 Q78,58 100,70" stroke="rgba(129,140,248,0.1)" stroke-width="0.6" fill="none"/>
            <path d="M100,70 Q128,52 155,64" stroke="rgba(129,140,248,0.1)" stroke-width="0.6" fill="none"/>

            <!-- expanding ripple rings -->
            <circle cx="55" cy="75" fill="none" stroke="#818cf8" stroke-width="0.4">
              <animate attributeName="r" values="5;22" dur="3.5s" repeatCount="indefinite"/>
              <animate attributeName="opacity" values="0.18;0" dur="3.5s" repeatCount="indefinite"/>
            </circle>
            <circle cx="100" cy="70" fill="none" stroke="#818cf8" stroke-width="0.4">
              <animate attributeName="r" values="10;32" dur="4s" repeatCount="indefinite" begin="0.6s"/>
              <animate attributeName="opacity" values="0.14;0" dur="4s" repeatCount="indefinite" begin="0.6s"/>
            </circle>
            <circle cx="155" cy="64" fill="none" stroke="#818cf8" stroke-width="0.4">
              <animate attributeName="r" values="16;42" dur="4.5s" repeatCount="indefinite" begin="1.2s"/>
              <animate attributeName="opacity" values="0.1;0" dur="4.5s" repeatCount="indefinite" begin="1.2s"/>
            </circle>

            <!-- soft glow halos -->
            <circle cx="55" cy="75" r="14" fill="#818cf8" filter="url(#grows-blur-sm)">
              <animate attributeName="opacity" values="0.03;0.08;0.03" dur="4s" repeatCount="indefinite"/>
            </circle>
            <circle cx="100" cy="70" r="22" fill="#818cf8" filter="url(#grows-blur-md)">
              <animate attributeName="opacity" values="0.03;0.07;0.03" dur="4s" repeatCount="indefinite" begin="0.6s"/>
            </circle>
            <circle cx="155" cy="64" r="30" fill="#818cf8" filter="url(#grows-blur-lg)">
              <animate attributeName="opacity" values="0.02;0.06;0.02" dur="4s" repeatCount="indefinite" begin="1.2s"/>
            </circle>

            <!-- main spheres with breathing pulse -->
            <circle cx="55" cy="75" fill="rgba(129,140,248,0.15)" stroke="rgba(129,140,248,0.1)" stroke-width="0.8">
              <animate attributeName="r" values="5;5.7;5" dur="4s" repeatCount="indefinite"/>
            </circle>
            <circle cx="100" cy="70" fill="rgba(129,140,248,0.15)" stroke="rgba(129,140,248,0.1)" stroke-width="0.8">
              <animate attributeName="r" values="10;11.2;10" dur="4s" repeatCount="indefinite" begin="0.6s"/>
            </circle>
            <circle cx="155" cy="64" fill="rgba(129,140,248,0.15)" stroke="rgba(129,140,248,0.1)" stroke-width="0.8">
              <animate attributeName="r" values="16;17.8;16" dur="4s" repeatCount="indefinite" begin="1.2s"/>
            </circle>

            <!-- traveling signals along arcs -->
            <circle r="1.3" fill="#818cf8" opacity="0.5">
              <animateMotion dur="2.5s" repeatCount="indefinite" path="M55,75 Q78,58 100,70"/>
            </circle>
            <circle r="1.3" fill="#a5b4fc" opacity="0.4">
              <animateMotion dur="3s" repeatCount="indefinite" path="M100,70 Q128,52 155,64" begin="1s"/>
            </circle>

            <!-- ghost 4th sphere — growth continues -->
            <path d="M171,64 Q190,48 210,56" stroke="rgba(129,140,248,0.06)" stroke-width="0.4" fill="none">
              <animate attributeName="opacity" values="0;1;1;0" keyTimes="0;0.3;0.65;1" dur="7s" repeatCount="indefinite" begin="2s"/>
            </path>
            <circle cx="210" cy="56" fill="rgba(129,140,248,0.06)" stroke="rgba(129,140,248,0.04)" stroke-width="0.5">
              <animate attributeName="r" values="0;10;10;0" keyTimes="0;0.3;0.65;1" dur="7s" repeatCount="indefinite" begin="2s"/>
            </circle>
          </svg>
        </div>
      </div>
    </section>

    <!-- ═══════ TIME TRAVEL ═══════ -->
    <section class="ino-timetravel">
      <div class="ino-inner">
        <div class="ino-label reveal">Flagship</div>
        <h2 class="ino-heading ino-heading--xl reveal">Time Travel.</h2>
        <p class="ino-body reveal">
          Trace how your system evolved.<br>
          See what happened, when it happened, and why.<br>
          Inspect the full history of intelligence.
        </p>
        <div class="ino-timeline reveal" aria-hidden="true">
          <svg viewBox="0 0 360 40" class="ino-timeline-svg">
            <defs>
              <linearGradient id="tt-track" x1="0" y1="0" x2="1" y2="0">
                <stop offset="0%" stop-color="#818cf8" stop-opacity="0"/>
                <stop offset="50%" stop-color="#818cf8" stop-opacity="0.4"/>
                <stop offset="100%" stop-color="#818cf8" stop-opacity="0"/>
              </linearGradient>
              <filter id="tt-blur"><feGaussianBlur stdDeviation="4"/></filter>
            </defs>

            <!-- track line -->
            <line x1="20" y1="20" x2="340" y2="20" stroke="url(#tt-track)" stroke-width="1.5"/>

            <!-- past event dots -->
            <circle cx="70" cy="20" r="3" fill="rgba(129,140,248,0.25)"/>
            <circle cx="150" cy="20" r="3" fill="rgba(129,140,248,0.25)"/>
            <circle cx="230" cy="20" r="3" fill="rgba(129,140,248,0.25)"/>

            <!-- "now" dot — pulsing glow -->
            <circle cx="310" cy="20" r="14" fill="#818cf8" filter="url(#tt-blur)">
              <animate attributeName="opacity" values="0.15;0.35;0.15" dur="3s" repeatCount="indefinite"/>
            </circle>
            <circle cx="310" cy="20" fill="#818cf8">
              <animate attributeName="r" values="5;6;5" dur="3s" repeatCount="indefinite"/>
            </circle>

            <!-- traveling sphere — scrubs back and forth across timeline -->
            <circle r="10" fill="#818cf8" filter="url(#tt-blur)" opacity="0.12">
              <animateMotion dur="6s" repeatCount="indefinite"
                path="M30,20 L330,20"
                keyPoints="0;1;0" keyTimes="0;0.5;1"
                calcMode="spline" keySplines="0.42 0 0.58 1;0.42 0 0.58 1"/>
            </circle>
            <circle r="3.5" fill="#a5b4fc">
              <animate attributeName="opacity" values="0.5;0.8;0.5" dur="2s" repeatCount="indefinite"/>
              <animateMotion dur="6s" repeatCount="indefinite"
                path="M30,20 L330,20"
                keyPoints="0;1;0" keyTimes="0;0.5;1"
                calcMode="spline" keySplines="0.42 0 0.58 1;0.42 0 0.58 1"/>
            </circle>
          </svg>
        </div>
      </div>
    </section>

    <!-- ═══════ PARALLEL UNIVERSES ═══════ -->
    <section class="ino-parallel">
      <div class="ino-inner">
        <div class="ino-label reveal">Flagship</div>
        <h2 class="ino-heading ino-heading--xl reveal">Parallel Universes.</h2>
        <p class="ino-body reveal">
          Fork any moment. Change one event. Run the simulation forward.<br>
          Compare what actually happened with what could have been.
        </p>
        <div class="ino-parallel-visual reveal" aria-hidden="true">
          <svg viewBox="0 0 360 80" class="ino-parallel-svg">
            <defs>
              <filter id="pu-blur"><feGaussianBlur stdDeviation="4"/></filter>
            </defs>

            <!-- shared history track -->
            <line x1="20" y1="40" x2="130" y2="40" stroke="rgba(129,140,248,0.25)" stroke-width="1.5"/>

            <!-- upper branch — original timeline -->
            <path d="M130,40 C158,40 168,22 190,22" stroke="rgba(129,140,248,0.25)" stroke-width="1.5" fill="none"/>
            <line x1="190" y1="22" x2="340" y2="22" stroke="rgba(129,140,248,0.25)" stroke-width="1.5"/>

            <!-- lower branch — simulation (dashed) -->
            <path d="M130,40 C158,40 168,58 190,58" stroke="rgba(167,139,250,0.18)" stroke-width="1" fill="none" stroke-dasharray="5 3"/>
            <line x1="190" y1="58" x2="340" y2="58" stroke="rgba(167,139,250,0.18)" stroke-width="1" stroke-dasharray="5 3"/>

            <!-- shared history dots -->
            <circle cx="55" cy="40" r="2.5" fill="rgba(129,140,248,0.3)"/>
            <circle cx="95" cy="40" r="2.5" fill="rgba(129,140,248,0.3)"/>

            <!-- fork checkpoint — pulsing glow -->
            <circle cx="130" cy="40" r="14" fill="#818cf8" filter="url(#pu-blur)">
              <animate attributeName="opacity" values="0.12;0.28;0.12" dur="3.5s" repeatCount="indefinite"/>
            </circle>
            <circle cx="130" cy="40" fill="#818cf8">
              <animate attributeName="r" values="4.5;5.5;4.5" dur="3.5s" repeatCount="indefinite"/>
            </circle>

            <!-- upper branch dots (actual events) -->
            <circle cx="230" cy="22" r="2.5" fill="rgba(129,140,248,0.3)"/>
            <circle cx="275" cy="22" r="2.5" fill="rgba(129,140,248,0.3)"/>
            <circle cx="320" cy="22" r="2.5" fill="rgba(129,140,248,0.3)"/>

            <!-- lower branch dots (simulation — one changed) -->
            <circle cx="230" cy="58" r="2.5" fill="rgba(167,139,250,0.25)"/>
            <circle cx="275" cy="58" fill="#a78bfa">
              <animate attributeName="r" values="3;3.6;3" dur="2.5s" repeatCount="indefinite"/>
              <animate attributeName="opacity" values="0.5;0.8;0.5" dur="2.5s" repeatCount="indefinite"/>
            </circle>
            <circle cx="320" cy="58" r="2.5" fill="rgba(167,139,250,0.25)"/>

            <!-- traveling sphere — original timeline -->
            <circle r="8" fill="#818cf8" filter="url(#pu-blur)" opacity="0.1">
              <animateMotion dur="5s" repeatCount="indefinite"
                path="M130,40 C158,40 168,22 190,22 L340,22"
                keyPoints="0;1;0" keyTimes="0;0.5;1"
                calcMode="spline" keySplines="0.42 0 0.58 1;0.42 0 0.58 1"/>
            </circle>
            <circle r="3" fill="#818cf8" opacity="0.55">
              <animateMotion dur="5s" repeatCount="indefinite"
                path="M130,40 C158,40 168,22 190,22 L340,22"
                keyPoints="0;1;0" keyTimes="0;0.5;1"
                calcMode="spline" keySplines="0.42 0 0.58 1;0.42 0 0.58 1"/>
            </circle>

            <!-- traveling sphere — simulation branch -->
            <circle r="8" fill="#a78bfa" filter="url(#pu-blur)" opacity="0.08">
              <animateMotion dur="5.5s" repeatCount="indefinite"
                path="M130,40 C158,40 168,58 190,58 L340,58"
                keyPoints="0;1;0" keyTimes="0;0.5;1"
                calcMode="spline" keySplines="0.42 0 0.58 1;0.42 0 0.58 1"
                begin="0.8s"/>
            </circle>
            <circle r="3" fill="#a78bfa" opacity="0.45">
              <animateMotion dur="5.5s" repeatCount="indefinite"
                path="M130,40 C158,40 168,58 190,58 L340,58"
                keyPoints="0;1;0" keyTimes="0;0.5;1"
                calcMode="spline" keySplines="0.42 0 0.58 1;0.42 0 0.58 1"
                begin="0.8s"/>
            </circle>
          </svg>
        </div>
      </div>
    </section>

    <!-- ═══════ FUTURE VISION ═══════ -->
    <section class="ino-future">
      <div class="ino-inner">
        <h2 class="ino-heading reveal">Toward shared intelligence.</h2>
        <p class="ino-body ino-body--muted reveal">
          Public neurons. Shared synapses. Collaborative learning across systems.<br>
          A future where intelligence compounds.
        </p>
        <div class="ino-future-visual reveal" aria-hidden="true">
          <svg viewBox="0 0 400 130" class="ino-future-svg">
            <defs>
              <filter id="future-blur"><feGaussianBlur stdDeviation="3"/></filter>
              <filter id="future-blur-lg"><feGaussianBlur stdDeviation="5"/></filter>
            </defs>

            <!-- ── connection mesh ── -->
            <!-- hub-to-hub arcs -->
            <path d="M120,80 Q160,28 200,26" stroke="rgba(129,140,248,0.12)" stroke-width="0.7" fill="none"/>
            <path d="M200,26 Q240,28 280,80" stroke="rgba(129,140,248,0.12)" stroke-width="0.7" fill="none"/>
            <path d="M120,80 Q200,100 280,80" stroke="rgba(129,140,248,0.1)" stroke-width="0.6" fill="none"/>
            <!-- hub-to-satellite -->
            <line x1="55" y1="48" x2="120" y2="80" stroke="rgba(129,140,248,0.07)" stroke-width="0.5"/>
            <line x1="80" y1="112" x2="120" y2="80" stroke="rgba(129,140,248,0.07)" stroke-width="0.5"/>
            <line x1="155" y1="50" x2="120" y2="80" stroke="rgba(129,140,248,0.06)" stroke-width="0.4"/>
            <line x1="155" y1="50" x2="200" y2="26" stroke="rgba(129,140,248,0.06)" stroke-width="0.4"/>
            <line x1="245" y1="50" x2="200" y2="26" stroke="rgba(129,140,248,0.06)" stroke-width="0.4"/>
            <line x1="245" y1="50" x2="280" y2="80" stroke="rgba(129,140,248,0.06)" stroke-width="0.4"/>
            <line x1="345" y1="48" x2="280" y2="80" stroke="rgba(129,140,248,0.07)" stroke-width="0.5"/>
            <line x1="320" y1="112" x2="280" y2="80" stroke="rgba(129,140,248,0.07)" stroke-width="0.5"/>
            <line x1="200" y1="108" x2="120" y2="80" stroke="rgba(129,140,248,0.05)" stroke-width="0.4"/>
            <line x1="200" y1="108" x2="280" y2="80" stroke="rgba(129,140,248,0.05)" stroke-width="0.4"/>
            <!-- cross satellite links -->
            <line x1="55" y1="48" x2="155" y2="50" stroke="rgba(129,140,248,0.04)" stroke-width="0.3"/>
            <line x1="245" y1="50" x2="345" y2="48" stroke="rgba(129,140,248,0.04)" stroke-width="0.3"/>
            <line x1="80" y1="112" x2="200" y2="108" stroke="rgba(129,140,248,0.04)" stroke-width="0.3"/>
            <line x1="200" y1="108" x2="320" y2="112" stroke="rgba(129,140,248,0.04)" stroke-width="0.3"/>

            <!-- ── expanding rings on hubs ── -->
            <circle cx="120" cy="80" fill="none" stroke="#818cf8" stroke-width="0.3">
              <animate attributeName="r" values="8;26" dur="4s" repeatCount="indefinite"/>
              <animate attributeName="opacity" values="0.15;0" dur="4s" repeatCount="indefinite"/>
            </circle>
            <circle cx="200" cy="26" fill="none" stroke="#818cf8" stroke-width="0.3">
              <animate attributeName="r" values="9;28" dur="4.5s" repeatCount="indefinite" begin="0.8s"/>
              <animate attributeName="opacity" values="0.12;0" dur="4.5s" repeatCount="indefinite" begin="0.8s"/>
            </circle>
            <circle cx="280" cy="80" fill="none" stroke="#818cf8" stroke-width="0.3">
              <animate attributeName="r" values="8;26" dur="4s" repeatCount="indefinite" begin="1.5s"/>
              <animate attributeName="opacity" values="0.15;0" dur="4s" repeatCount="indefinite" begin="1.5s"/>
            </circle>

            <!-- ── glow halos on hubs ── -->
            <circle cx="120" cy="80" r="20" fill="#818cf8" filter="url(#future-blur-lg)">
              <animate attributeName="opacity" values="0.04;0.1;0.04" dur="4s" repeatCount="indefinite"/>
            </circle>
            <circle cx="200" cy="26" r="22" fill="#818cf8" filter="url(#future-blur-lg)">
              <animate attributeName="opacity" values="0.04;0.11;0.04" dur="4.5s" repeatCount="indefinite" begin="0.8s"/>
            </circle>
            <circle cx="280" cy="80" r="20" fill="#818cf8" filter="url(#future-blur-lg)">
              <animate attributeName="opacity" values="0.04;0.1;0.04" dur="4s" repeatCount="indefinite" begin="1.5s"/>
            </circle>

            <!-- ── hub nodes (orbit ring + breathing core) ── -->
            <circle cx="120" cy="80" r="14" fill="none" stroke="rgba(129,140,248,0.07)" stroke-width="0.4"/>
            <circle cx="120" cy="80" fill="rgba(129,140,248,0.2)" stroke="rgba(129,140,248,0.15)" stroke-width="0.8">
              <animate attributeName="r" values="8;8.8;8" dur="4s" repeatCount="indefinite"/>
            </circle>

            <circle cx="200" cy="26" r="16" fill="none" stroke="rgba(129,140,248,0.07)" stroke-width="0.4"/>
            <circle cx="200" cy="26" fill="rgba(129,140,248,0.2)" stroke="rgba(129,140,248,0.15)" stroke-width="0.8">
              <animate attributeName="r" values="9;9.9;9" dur="4.5s" repeatCount="indefinite" begin="0.8s"/>
            </circle>

            <circle cx="280" cy="80" r="14" fill="none" stroke="rgba(129,140,248,0.07)" stroke-width="0.4"/>
            <circle cx="280" cy="80" fill="rgba(129,140,248,0.2)" stroke="rgba(129,140,248,0.15)" stroke-width="0.8">
              <animate attributeName="r" values="8;8.8;8" dur="4s" repeatCount="indefinite" begin="1.5s"/>
            </circle>

            <!-- ── satellite nodes (breathing) ── -->
            <circle cx="55" cy="48" r="3" fill="rgba(129,140,248,0.12)" stroke="rgba(129,140,248,0.08)" stroke-width="0.5">
              <animate attributeName="opacity" values="0.6;1;0.6" dur="5s" repeatCount="indefinite"/>
            </circle>
            <circle cx="80" cy="112" r="2.5" fill="rgba(129,140,248,0.1)" stroke="rgba(129,140,248,0.06)" stroke-width="0.4">
              <animate attributeName="opacity" values="0.5;0.9;0.5" dur="6s" repeatCount="indefinite" begin="1s"/>
            </circle>
            <circle cx="155" cy="50" r="3.5" fill="rgba(129,140,248,0.12)" stroke="rgba(129,140,248,0.08)" stroke-width="0.5">
              <animate attributeName="opacity" values="0.7;1;0.7" dur="4.5s" repeatCount="indefinite" begin="0.5s"/>
            </circle>
            <circle cx="245" cy="50" r="3.5" fill="rgba(129,140,248,0.12)" stroke="rgba(129,140,248,0.08)" stroke-width="0.5">
              <animate attributeName="opacity" values="0.7;1;0.7" dur="4.5s" repeatCount="indefinite" begin="1.2s"/>
            </circle>
            <circle cx="345" cy="48" r="3" fill="rgba(129,140,248,0.12)" stroke="rgba(129,140,248,0.08)" stroke-width="0.5">
              <animate attributeName="opacity" values="0.6;1;0.6" dur="5s" repeatCount="indefinite" begin="0.8s"/>
            </circle>
            <circle cx="320" cy="112" r="2.5" fill="rgba(129,140,248,0.1)" stroke="rgba(129,140,248,0.06)" stroke-width="0.4">
              <animate attributeName="opacity" values="0.5;0.9;0.5" dur="6s" repeatCount="indefinite" begin="2s"/>
            </circle>
            <circle cx="200" cy="108" r="3" fill="rgba(129,140,248,0.1)" stroke="rgba(129,140,248,0.06)" stroke-width="0.5">
              <animate attributeName="opacity" values="0.6;1;0.6" dur="5.5s" repeatCount="indefinite" begin="1.5s"/>
            </circle>

            <!-- ── traveling signals (busy network) ── -->
            <!-- hub-to-hub -->
            <circle r="1.5" fill="#818cf8" opacity="0.5">
              <animateMotion dur="3s" repeatCount="indefinite" path="M120,80 Q160,28 200,26"/>
            </circle>
            <circle r="1.5" fill="#a5b4fc" opacity="0.4">
              <animateMotion dur="3.5s" repeatCount="indefinite" path="M200,26 Q240,28 280,80" begin="0.6s"/>
            </circle>
            <circle r="1.5" fill="#818cf8" opacity="0.35">
              <animateMotion dur="4s" repeatCount="indefinite" path="M280,80 Q200,100 120,80" begin="1.2s"/>
            </circle>
            <!-- hub-to-satellite -->
            <circle r="1" fill="#a5b4fc" opacity="0.3">
              <animateMotion dur="2.5s" repeatCount="indefinite" path="M55,48 L120,80" begin="0.3s"/>
            </circle>
            <circle r="1" fill="#818cf8" opacity="0.3">
              <animateMotion dur="2.5s" repeatCount="indefinite" path="M280,80 L345,48" begin="1.8s"/>
            </circle>
            <circle r="1" fill="#a5b4fc" opacity="0.25">
              <animateMotion dur="3s" repeatCount="indefinite" path="M200,108 L280,80" begin="2.5s"/>
            </circle>

            <!-- ── ghost nodes — network expanding ── -->
            <circle cx="28" cy="25" fill="rgba(129,140,248,0.06)" stroke="rgba(129,140,248,0.04)" stroke-width="0.3">
              <animate attributeName="r" values="0;3;3;0" keyTimes="0;0.3;0.7;1" dur="8s" repeatCount="indefinite" begin="3s"/>
            </circle>
            <line x1="28" y1="25" x2="55" y2="48" stroke="rgba(129,140,248,0.04)" stroke-width="0.3">
              <animate attributeName="opacity" values="0;1;1;0" keyTimes="0;0.3;0.7;1" dur="8s" repeatCount="indefinite" begin="3s"/>
            </line>
            <circle cx="372" cy="25" fill="rgba(129,140,248,0.06)" stroke="rgba(129,140,248,0.04)" stroke-width="0.3">
              <animate attributeName="r" values="0;3;3;0" keyTimes="0;0.3;0.7;1" dur="8s" repeatCount="indefinite" begin="6s"/>
            </circle>
            <line x1="372" y1="25" x2="345" y2="48" stroke="rgba(129,140,248,0.04)" stroke-width="0.3">
              <animate attributeName="opacity" values="0;1;1;0" keyTimes="0;0.3;0.7;1" dur="8s" repeatCount="indefinite" begin="6s"/>
            </line>
          </svg>
        </div>
      </div>
    </section>

    <!-- ═══════ CLOSING ═══════ -->
    <section class="ino-closing">
      <div class="ino-inner">
        <p class="ino-closing-lead reveal">It's not artificial general intelligence.</p>
        <p class="ino-closing-punch reveal">It's just general intelligence.</p>
      </div>
    </section>

  </div>
</template>

<style scoped>
/* ─── Layout ─── */
.ino-home {
  position: relative;
  z-index: 1;
  color: #fff;
  -webkit-font-smoothing: antialiased;
  -moz-osx-font-smoothing: grayscale;
}

.ino-inner {
  max-width: 900px;
  margin: 0 auto;
  padding: 0 2rem;
  text-align: center;
}

/* ─── Typography ─── */
.ino-heading {
  font-size: clamp(1.8rem, 4.5vw, 3rem);
  font-weight: 700;
  letter-spacing: -0.03em;
  color: #fff;
  margin: 0 0 1.5rem;
  line-height: 1.15;
}

.ino-heading--xl {
  font-size: clamp(2.5rem, 7vw, 4.5rem);
}

.ino-body {
  font-size: clamp(1.05rem, 1.8vw, 1.25rem);
  color: rgba(255, 255, 255, 0.45);
  line-height: 1.8;
  margin: 0;
}

.ino-body--muted {
  color: rgba(255, 255, 255, 0.32);
}

.ino-label {
  display: inline-block;
  font-size: 0.72rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.16em;
  color: #818cf8;
  margin-bottom: 1.5rem;
}

/* ─── Hero ─── */
.ino-hero {
  position: relative;
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #000;
  overflow: hidden;
}

.ino-hero-mesh {
  position: absolute;
  inset: 0;
  pointer-events: none;
}

.ino-hero-mesh svg {
  width: 100%;
  height: 100%;
}

.ino-hero-glow {
  position: absolute;
  width: 50vw;
  max-width: 700px;
  aspect-ratio: 1;
  border-radius: 50%;
  background: radial-gradient(circle, rgba(99, 102, 241, 0.12) 0%, transparent 70%);
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  filter: blur(80px);
  pointer-events: none;
}

.ino-hero-content {
  position: relative;
  z-index: 1;
  text-align: center;
  padding: 2rem;
  max-width: 800px;
}

.ino-hero-brand {
  font-size: clamp(5rem, 15vw, 10rem);
  font-weight: 800;
  letter-spacing: -0.05em;
  background: linear-gradient(160deg, #fff 40%, #a5b4fc 100%);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
  margin: 0 0 1.5rem;
  line-height: 1;
}

.ino-hero-headline {
  font-size: clamp(1.35rem, 3.5vw, 2.2rem);
  font-weight: 500;
  color: rgba(255, 255, 255, 0.88);
  margin: 0 0 0.75rem;
  letter-spacing: -0.02em;
}

.ino-hero-sub {
  font-size: clamp(1rem, 2vw, 1.2rem);
  font-weight: 400;
  color: rgba(255, 255, 255, 0.4);
  margin: 0 0 3rem;
  line-height: 1.7;
}

.ino-hero-ctas {
  display: flex;
  gap: 1rem;
  justify-content: center;
  flex-wrap: wrap;
}

.ino-cta-primary {
  display: inline-flex;
  align-items: center;
  padding: 0.75rem 2rem;
  background: #fff;
  color: #000;
  font-weight: 600;
  font-size: 0.95rem;
  border-radius: 980px;
  text-decoration: none;
  transition: background 0.25s ease, transform 0.25s ease, box-shadow 0.25s ease;
}

.ino-cta-primary:hover {
  background: rgba(255, 255, 255, 0.88);
  transform: translateY(-1px);
  box-shadow: 0 4px 24px rgba(255, 255, 255, 0.08);
}

.ino-cta-secondary {
  display: inline-flex;
  align-items: center;
  padding: 0.75rem 2rem;
  color: rgba(255, 255, 255, 0.55);
  font-weight: 500;
  font-size: 0.95rem;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 980px;
  text-decoration: none;
  transition: color 0.25s ease, border-color 0.25s ease;
}

.ino-cta-secondary:hover {
  color: #fff;
  border-color: rgba(255, 255, 255, 0.28);
}

.ino-hero-scroll {
  position: absolute;
  bottom: 2.5rem;
  left: 50%;
  transform: translateX(-50%);
  color: rgba(255, 255, 255, 0.18);
  animation: ino-bounce 2.4s ease-in-out infinite;
}

/* ─── Primitives ─── */
.ino-primitives {
  background: #050506;
  padding: 10rem 0;
}

.ino-primitives-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1.5rem;
  margin-top: 3.5rem;
}

.ino-card {
  background: rgba(255, 255, 255, 0.025);
  border: 1px solid rgba(255, 255, 255, 0.055);
  border-radius: 20px;
  padding: 3rem 2.5rem;
  text-align: center;
  transition: border-color 0.35s ease;
}

.ino-card:hover {
  border-color: rgba(255, 255, 255, 0.12);
}

.ino-card-icon {
  margin-bottom: 1.5rem;
  display: flex;
  justify-content: center;
}

.ino-card h3 {
  font-size: 1.5rem;
  font-weight: 600;
  margin: 0 0 0.75rem;
  letter-spacing: -0.02em;
  color: #fff;
}

.ino-card p {
  font-size: 0.98rem;
  color: rgba(255, 255, 255, 0.42);
  line-height: 1.7;
  margin: 0;
}

/* ─── Genesis ─── */
.ino-genesis {
  background: #000;
  padding: 10rem 0;
}

.ino-genesis-visual {
  margin-top: 3.5rem;
  display: flex;
  justify-content: center;
}

.ino-genesis-svg {
  width: min(640px, 92vw);
  height: auto;
  max-height: 60vh;
}

/* Edges — hidden by default, fade in when frame threshold is met */
.genesis-edge {
  stroke: rgba(129, 140, 248, 0.35);
  stroke-width: 0.6;
  fill: none;
  opacity: 0;
  transition: opacity 0.8s cubic-bezier(0.16, 1, 0.3, 1);
}
.genesis-edge.visible { opacity: 1; }
.genesis-edge-primary {
  stroke: rgba(245, 158, 11, 0.55);
  stroke-width: 1;
}

/* Nodes — scale + opacity reveal */
.genesis-node-group {
  opacity: 0;
  transform: scale(0.4);
  transform-origin: center;
  transform-box: fill-box;
  transition: opacity 0.9s cubic-bezier(0.16, 1, 0.3, 1),
              transform 0.9s cubic-bezier(0.16, 1, 0.3, 1);
}
.genesis-node-group.visible {
  opacity: 1;
  transform: scale(1);
}

.genesis-node {
  fill: rgba(165, 180, 252, 0.85);
  stroke: rgba(199, 210, 254, 0.7);
  stroke-width: 0.6;
}
.kind-creator .genesis-node {
  fill: #fcd34d;
  stroke: #fde68a;
  stroke-width: 1;
}
.kind-ino .genesis-node {
  fill: #a5b4fc;
  stroke: #c7d2fe;
  stroke-width: 1.2;
  filter: drop-shadow(0 0 6px rgba(129, 140, 248, 0.55));
}
.kind-domain .genesis-node {
  fill: rgba(129, 140, 248, 0.9);
  stroke: rgba(199, 210, 254, 0.8);
}
.kind-app .genesis-node {
  fill: rgba(165, 180, 252, 0.7);
}

.genesis-node-label {
  fill: rgba(255, 255, 255, 0.55);
  font-family: var(--vp-font-family-base);
  font-size: 8px;
  font-weight: 500;
  pointer-events: none;
}
.kind-creator .genesis-node-label {
  fill: rgba(253, 230, 138, 0.9);
  font-size: 9px;
  font-weight: 600;
}
.kind-ino .genesis-node-label {
  fill: rgba(199, 210, 254, 0.95);
  font-size: 10px;
  font-weight: 700;
}
.kind-domain .genesis-node-label {
  fill: rgba(199, 210, 254, 0.85);
  font-size: 8.5px;
  font-weight: 600;
}

/* Signal pulses: only run once their edge exists */
.genesis-pulse-off { visibility: hidden; }
.genesis-pulse-on  { visibility: visible; }

/* Caption / frame dots */
.ino-genesis-caption {
  margin-top: 2rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
}
.ino-genesis-dots {
  display: inline-flex;
  gap: 6px;
}
.ino-genesis-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: rgba(129, 140, 248, 0.2);
  transition: background 0.4s ease, transform 0.4s ease;
}
.ino-genesis-dot.active {
  background: #818cf8;
  transform: scale(1.5);
  box-shadow: 0 0 8px rgba(129, 140, 248, 0.6);
}
.ino-genesis-step {
  font-size: 0.9rem;
  color: rgba(255, 255, 255, 0.45);
  font-weight: 500;
  letter-spacing: 0.01em;
  font-family: var(--vp-font-family-base);
  min-height: 1.4em;
}

/* ─── Grows ─── */
.ino-grows {
  background: #000;
  padding: 10rem 0;
}

.ino-grows-visual {
  margin-top: 3rem;
  display: flex;
  justify-content: center;
}

.ino-grows-svg {
  width: min(260px, 70vw);
  height: auto;
}

/* ─── Time Travel ─── */
.ino-timetravel {
  background: #050506;
  padding: 10rem 0;
}

.ino-timetravel .ino-heading {
  text-shadow: 0 0 80px rgba(129, 140, 248, 0.25);
}

.ino-timeline {
  margin-top: 3.5rem;
  display: flex;
  justify-content: center;
}

.ino-timeline-svg {
  width: min(80%, 360px);
  height: auto;
}

/* ─── Parallel Universes ─── */
.ino-parallel {
  background: #000;
  padding: 10rem 0;
}

.ino-parallel .ino-heading {
  text-shadow: 0 0 80px rgba(167, 139, 250, 0.2);
}

.ino-parallel .ino-label {
  color: #a78bfa;
}

.ino-parallel-visual {
  margin-top: 3.5rem;
  display: flex;
  justify-content: center;
}

.ino-parallel-svg {
  width: min(80%, 360px);
  height: auto;
}

/* ─── Future ─── */
.ino-future {
  background: #000;
  padding: 8rem 0;
}

.ino-future-visual {
  margin-top: 3rem;
  display: flex;
  justify-content: center;
}

.ino-future-svg {
  width: min(400px, 85vw);
  height: auto;
}

/* ─── Closing ─── */
.ino-closing {
  background: #000;
  padding: 10rem 0 10rem;
  border-top: 1px solid rgba(255, 255, 255, 0.04);
}

.ino-closing-lead {
  font-size: clamp(1.2rem, 2.8vw, 1.8rem);
  font-weight: 500;
  color: rgba(255, 255, 255, 0.4);
  margin: 0 0 1rem;
  letter-spacing: -0.01em;
  line-height: 1.3;
}

.ino-closing-punch {
  font-size: clamp(1.8rem, 4.5vw, 3.2rem);
  font-weight: 700;
  margin: 0;
  letter-spacing: -0.03em;
  line-height: 1.2;
  background: linear-gradient(135deg, #fff 30%, #a5b4fc);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}

/* ─── Scroll reveal ─── */
.reveal {
  opacity: 0;
  transform: translateY(28px);
  transition: opacity 0.9s cubic-bezier(0.16, 1, 0.3, 1),
              transform 0.9s cubic-bezier(0.16, 1, 0.3, 1);
  will-change: opacity, transform;
}

.reveal.in-view {
  opacity: 1;
  transform: translateY(0);
}

.reveal.delay-1 {
  transition-delay: 0.15s;
}

/* ─── Mesh node pulse ─── */
@keyframes ino-node-pulse {
  0%, 100% { opacity: 0.15; }
  50% { opacity: 0.45; }
}

.mesh-node { animation: ino-node-pulse 5s ease-in-out infinite; }
.n1  { animation-delay: 0s; }
.n2  { animation-delay: 0.4s; }
.n3  { animation-delay: 0.9s; }
.n4  { animation-delay: 1.3s; }
.n5  { animation-delay: 1.7s; }
.n6  { animation-delay: 2.1s; }
.n7  { animation-delay: 0.2s; }
.n8  { animation-delay: 0.7s; }
.n9  { animation-delay: 1.1s; }
.n10 { animation-delay: 1.5s; }
.n11 { animation-delay: 1.9s; }
.n12 { animation-delay: 2.3s; }
.n13 { animation-delay: 0.3s; }
.n14 { animation-delay: 0.8s; }
.n15 { animation-delay: 1.2s; }
.n16 { animation-delay: 1.6s; }

/* ─── Scroll hint bounce ─── */
@keyframes ino-bounce {
  0%, 100% { transform: translateX(-50%) translateY(0); opacity: 0.18; }
  50% { transform: translateX(-50%) translateY(8px); opacity: 0.4; }
}

/* ─── Responsive ─── */
@media (max-width: 768px) {
  .ino-primitives-grid {
    grid-template-columns: 1fr;
  }

  .ino-primitives,
  .ino-genesis,
  .ino-grows,
  .ino-timetravel,
  .ino-parallel,
  .ino-closing {
    padding: 6rem 0;
  }

  .ino-future {
    padding: 5rem 0;
  }

  .ino-card {
    padding: 2.5rem 2rem;
  }

  .ino-hero-sub br,
  .ino-body br,
  .ino-card p br {
    display: none;
  }

  .ino-timeline {
    gap: 2rem;
  }
}
</style>
