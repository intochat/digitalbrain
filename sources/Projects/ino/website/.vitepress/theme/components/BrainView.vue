<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'

type Readiness = 'READY' | 'PLANNED' | 'VISION'

interface AppNode {
  name: string
}

interface DomainNode {
  name: string
  apps: AppNode[]
  readyPct: number
}

// 25 domains from the spec. readiness bucket is derived from readyPct:
//   >= 80 -> READY (green), 40-79 -> PLANNED (yellow), < 40 -> VISION (gray)
const domains: DomainNode[] = [
  { name: 'Developer Tools', readyPct: 100, apps: [
    { name: 'GitHub' }, { name: 'GitLab' }, { name: 'VS Code' }, { name: 'Docker' },
    { name: 'Jira' }, { name: 'Vercel' }, { name: 'npm' }, { name: 'PyPI' },
  ]},
  { name: 'Productivity', readyPct: 100, apps: [
    { name: 'Notion' }, { name: 'Linear' }, { name: 'Asana' }, { name: 'Trello' },
    { name: 'Todoist' }, { name: 'Obsidian' }, { name: 'Evernote' }, { name: 'ClickUp' },
    { name: 'Monday' }, { name: 'Airtable' },
  ]},
  { name: 'Cloud Storage', readyPct: 80, apps: [
    { name: 'Google Drive' }, { name: 'Dropbox' }, { name: 'OneDrive' }, { name: 'iCloud' }, { name: 'Box' },
  ]},
  { name: 'Social Media', readyPct: 80, apps: [
    { name: 'X' }, { name: 'Instagram' }, { name: 'LinkedIn' }, { name: 'Facebook' },
    { name: 'Reddit' }, { name: 'TikTok' }, { name: 'Mastodon' }, { name: 'Bluesky' },
    { name: 'Pinterest' }, { name: 'Threads' },
  ]},
  { name: 'Messaging', readyPct: 80, apps: [
    { name: 'Telegram' }, { name: 'WhatsApp' }, { name: 'Signal' }, { name: 'Discord' },
    { name: 'Slack' }, { name: 'iMessage' }, { name: 'Messenger' }, { name: 'WeChat' },
    { name: 'Viber' }, { name: 'Line' },
  ]},
  { name: 'Finance', readyPct: 70, apps: [
    { name: 'Stripe' }, { name: 'PayPal' }, { name: 'Wise' }, { name: 'Revolut' },
    { name: 'Chase' }, { name: 'Robinhood' }, { name: 'Coinbase' }, { name: 'Binance' },
    { name: 'Plaid' }, { name: 'Mint' },
  ]},
  { name: 'Home & IoT', readyPct: 71, apps: [
    { name: 'Home Assistant' }, { name: 'Philips Hue' }, { name: 'Nest' },
    { name: 'Alexa' }, { name: 'SmartThings' }, { name: 'HomeKit' }, { name: 'Ring' },
  ]},
  { name: 'Shopping', readyPct: 60, apps: [
    { name: 'Amazon' }, { name: 'eBay' }, { name: 'Etsy' }, { name: 'Shopify' },
    { name: 'AliExpress' }, { name: 'Walmart' }, { name: 'Target' }, { name: 'Best Buy' },
    { name: 'IKEA' }, { name: 'Costco' },
  ]},
  { name: 'Navigation', readyPct: 67, apps: [
    { name: 'Google Maps' }, { name: 'Apple Maps' }, { name: 'Waze' },
    { name: 'OsmAnd' }, { name: 'Citymapper' }, { name: 'HERE WeGo' },
  ]},
  { name: 'Music', readyPct: 63, apps: [
    { name: 'Spotify' }, { name: 'Apple Music' }, { name: 'YouTube Music' },
    { name: 'Tidal' }, { name: 'SoundCloud' }, { name: 'Deezer' },
    { name: 'Pandora' }, { name: 'Amazon Music' },
  ]},
  { name: 'Weather', readyPct: 100, apps: [
    { name: 'AccuWeather' }, { name: 'Weather.com' }, { name: 'OpenWeather' }, { name: 'Windy' },
  ]},
  { name: 'Travel', readyPct: 50, apps: [
    { name: 'Booking.com' }, { name: 'Airbnb' }, { name: 'Expedia' }, { name: 'Kayak' },
    { name: 'Skyscanner' }, { name: 'Hotels.com' }, { name: 'Google Flights' },
    { name: 'TripAdvisor' }, { name: 'Hopper' }, { name: 'Agoda' },
  ]},
  { name: 'Fitness', readyPct: 50, apps: [
    { name: 'Strava' }, { name: 'MyFitnessPal' }, { name: 'Peloton' }, { name: 'Garmin' },
    { name: 'Fitbit' }, { name: 'Whoop' }, { name: 'Nike Run Club' }, { name: 'Apple Fitness' },
  ]},
  { name: 'Email', readyPct: 60, apps: [
    { name: 'Gmail' }, { name: 'Outlook' }, { name: 'Apple Mail' }, { name: 'Proton Mail' }, { name: 'Fastmail' },
  ]},
  { name: 'Creative', readyPct: 57, apps: [
    { name: 'Figma' }, { name: 'Photoshop' }, { name: 'Canva' }, { name: 'Procreate' },
    { name: 'Illustrator' }, { name: 'Blender' }, { name: 'Runway' },
  ]},
  { name: 'Education', readyPct: 50, apps: [
    { name: 'Coursera' }, { name: 'Udemy' }, { name: 'Khan Academy' }, { name: 'Duolingo' },
    { name: 'edX' }, { name: 'Brilliant' }, { name: 'Skillshare' }, { name: 'MasterClass' },
  ]},
  { name: 'Payments', readyPct: 38, apps: [
    { name: 'Apple Pay' }, { name: 'Google Pay' }, { name: 'Venmo' }, { name: 'Cash App' },
    { name: 'Zelle' }, { name: 'Klarna' }, { name: 'Afterpay' }, { name: 'Affirm' },
  ]},
  { name: 'Gaming', readyPct: 50, apps: [
    { name: 'Steam' }, { name: 'Xbox' }, { name: 'PlayStation' },
    { name: 'Epic Games' }, { name: 'Nintendo' }, { name: 'GOG' },
  ]},
  { name: 'News', readyPct: 43, apps: [
    { name: 'NYT' }, { name: 'WSJ' }, { name: 'Guardian' }, { name: 'BBC' },
    { name: 'Hacker News' }, { name: 'Bloomberg' }, { name: 'Reuters' },
  ]},
  { name: 'Grocery', readyPct: 43, apps: [
    { name: 'Instacart' }, { name: 'Whole Foods' }, { name: 'Kroger' }, { name: 'Tesco' },
    { name: 'Safeway' }, { name: 'Sainsbury\u2019s' }, { name: 'Carrefour' },
  ]},
  { name: 'Food Delivery', readyPct: 13, apps: [
    { name: 'DoorDash' }, { name: 'Uber Eats' }, { name: 'Grubhub' }, { name: 'Deliveroo' },
    { name: 'Postmates' }, { name: 'Just Eat' }, { name: 'Wolt' }, { name: 'Glovo' },
  ]},
  { name: 'Utilities', readyPct: 33, apps: [
    { name: 'Electricity' }, { name: 'Water' }, { name: 'Gas' },
    { name: 'Internet' }, { name: 'Phone' }, { name: 'Trash' },
  ]},
  { name: 'Transport', readyPct: 20, apps: [
    { name: 'Uber' }, { name: 'Lyft' }, { name: 'Bolt' }, { name: 'Grab' },
    { name: 'Didi' }, { name: 'Ola' }, { name: 'Gett' }, { name: 'FreeNow' },
    { name: 'Curb' }, { name: 'Yandex Go' },
  ]},
  { name: 'Video', readyPct: 25, apps: [
    { name: 'YouTube' }, { name: 'Netflix' }, { name: 'Disney+' }, { name: 'Prime Video' },
    { name: 'HBO Max' }, { name: 'Hulu' }, { name: 'Apple TV+' }, { name: 'Twitch' },
  ]},
  { name: 'Health', readyPct: 0, apps: [
    { name: 'Apple Health' }, { name: 'Google Fit' }, { name: 'MyChart' }, { name: 'Teladoc' },
    { name: 'Headspace' }, { name: 'Calm' }, { name: 'Zocdoc' }, { name: 'One Medical' },
  ]},
]

const totalApps = domains.reduce((sum, d) => sum + d.apps.length, 0)

function readinessOf(d: DomainNode): Readiness {
  if (d.readyPct >= 80) return 'READY'
  if (d.readyPct >= 40) return 'PLANNED'
  return 'VISION'
}

function readinessClass(d: DomainNode): string {
  return 'r-' + readinessOf(d).toLowerCase()
}

// ── Layout ────────────────────────────────────────────────────────
const RADIUS = 300
const APP_RADIUS = 80 // offset of apps around their selected domain

function angleFor(i: number, total: number): number {
  // start at -90° so first node sits at 12 o'clock
  return (i / total) * Math.PI * 2 - Math.PI / 2
}

function domainX(i: number): number {
  return Math.cos(angleFor(i, visibleDomains.value.length)) * RADIUS
}

function domainY(i: number): number {
  return Math.sin(angleFor(i, visibleDomains.value.length)) * RADIUS
}

function nodeRadius(d: DomainNode): number {
  // bigger clusters → bigger nodes
  return 16 + Math.min(d.apps.length, 10) * 0.9
}

// ── Filter ────────────────────────────────────────────────────────
const filter = ref<'all' | 'ready' | 'planned' | 'vision'>('all')

const visibleDomains = computed<DomainNode[]>(() => {
  if (filter.value === 'all') return domains
  const want: Readiness =
    filter.value === 'ready' ? 'READY' :
    filter.value === 'planned' ? 'PLANNED' : 'VISION'
  return domains.filter(d => readinessOf(d) === want)
})

// ── Selection ────────────────────────────────────────────────────
const selectedDomain = ref<DomainNode | null>(null)

function selectDomain(d: DomainNode) {
  selectedDomain.value = selectedDomain.value?.name === d.name ? null : d
}

const selectedIndex = computed(() => {
  if (!selectedDomain.value) return -1
  return visibleDomains.value.findIndex(d => d.name === selectedDomain.value!.name)
})

function appPos(appIndex: number, total: number): { x: number; y: number } {
  if (selectedIndex.value < 0) return { x: 0, y: 0 }
  const cx = domainX(selectedIndex.value)
  const cy = domainY(selectedIndex.value)
  const a = (appIndex / total) * Math.PI * 2 - Math.PI / 2
  return { x: cx + Math.cos(a) * APP_RADIUS, y: cy + Math.sin(a) * APP_RADIUS }
}

// ── Pan & Zoom ────────────────────────────────────────────────────
const zoom = ref(1)
const panX = ref(0)
const panY = ref(0)
const panning = ref(false)
const panStart = ref({ x: 0, y: 0, px: 0, py: 0 })

function onWheel(e: WheelEvent) {
  const delta = -e.deltaY * 0.0015
  const next = Math.max(0.4, Math.min(3, zoom.value * (1 + delta)))
  zoom.value = next
}

function onPanStart(e: MouseEvent) {
  panning.value = true
  panStart.value = { x: e.clientX, y: e.clientY, px: panX.value, py: panY.value }
}

function onPanMove(e: MouseEvent) {
  if (!panning.value) return
  panX.value = panStart.value.px + (e.clientX - panStart.value.x)
  panY.value = panStart.value.py + (e.clientY - panStart.value.y)
}

function onPanEnd() {
  panning.value = false
}

// Pinch-zoom on touch
const pinchStart = ref<{ dist: number; zoom: number } | null>(null)
function touchDist(t: TouchList): number {
  const dx = t[0].clientX - t[1].clientX
  const dy = t[0].clientY - t[1].clientY
  return Math.hypot(dx, dy)
}
function onTouchStart(e: TouchEvent) {
  if (e.touches.length === 2) {
    pinchStart.value = { dist: touchDist(e.touches), zoom: zoom.value }
  } else if (e.touches.length === 1) {
    panning.value = true
    panStart.value = { x: e.touches[0].clientX, y: e.touches[0].clientY, px: panX.value, py: panY.value }
  }
}
function onTouchMove(e: TouchEvent) {
  if (e.touches.length === 2 && pinchStart.value) {
    e.preventDefault()
    const d = touchDist(e.touches)
    const factor = d / pinchStart.value.dist
    zoom.value = Math.max(0.4, Math.min(3, pinchStart.value.zoom * factor))
  } else if (e.touches.length === 1 && panning.value) {
    panX.value = panStart.value.px + (e.touches[0].clientX - panStart.value.x)
    panY.value = panStart.value.py + (e.touches[0].clientY - panStart.value.y)
  }
}
function onTouchEnd() {
  pinchStart.value = null
  panning.value = false
}

function resetZoom() {
  zoom.value = 1
  panX.value = 0
  panY.value = 0
  selectedDomain.value = null
}

// ── Entrance reveal ───────────────────────────────────────────────
const wrapperRef = ref<HTMLElement | null>(null)
const visible = ref(false)
let obs: IntersectionObserver | null = null

onMounted(() => {
  obs = new IntersectionObserver(
    ([entry]) => {
      if (entry.isIntersecting) {
        visible.value = true
        obs?.disconnect()
      }
    },
    { threshold: 0.1 },
  )
  if (wrapperRef.value) obs.observe(wrapperRef.value)
})

onUnmounted(() => {
  obs?.disconnect()
  obs = null
})

// ── Readiness summary for toolbar label ──────────────────────────
const summary = computed(() => {
  const ready = domains.filter(d => readinessOf(d) === 'READY').length
  const planned = domains.filter(d => readinessOf(d) === 'PLANNED').length
  const vision = domains.filter(d => readinessOf(d) === 'VISION').length
  return { ready, planned, vision, total: domains.length, totalApps }
})
</script>

<template>
  <div ref="wrapperRef" class="brain-container" :class="{ visible }">
    <div class="brain-toolbar">
      <div class="brain-stats">
        <span class="stat"><strong>{{ summary.total }}</strong> domains</span>
        <span class="stat-sep">·</span>
        <span class="stat"><strong>{{ summary.totalApps }}</strong> apps</span>
      </div>
      <div class="brain-filters">
        <button class="filter-btn" :class="{ active: filter === 'all' }" @click="filter = 'all'">
          All
        </button>
        <button class="filter-btn r-ready" :class="{ active: filter === 'ready' }" @click="filter = 'ready'">
          <span class="dot"></span> Ready
        </button>
        <button class="filter-btn r-planned" :class="{ active: filter === 'planned' }" @click="filter = 'planned'">
          <span class="dot"></span> Planned
        </button>
        <button class="filter-btn r-vision" :class="{ active: filter === 'vision' }" @click="filter = 'vision'">
          <span class="dot"></span> Vision
        </button>
      </div>
      <button class="reset-btn" @click="resetZoom">⟲ Reset</button>
    </div>

    <div
      class="brain-canvas"
      :class="{ panning }"
      @wheel.prevent="onWheel"
      @mousedown="onPanStart"
      @mousemove="onPanMove"
      @mouseup="onPanEnd"
      @mouseleave="onPanEnd"
      @touchstart="onTouchStart"
      @touchmove.prevent="onTouchMove"
      @touchend="onTouchEnd"
    >
      <svg
        class="brain-svg"
        :style="{ transform: `translate(${panX}px, ${panY}px) scale(${zoom})` }"
        viewBox="-420 -420 840 840"
        xmlns="http://www.w3.org/2000/svg"
      >
        <defs>
          <radialGradient id="brain-core-grad" cx="50%" cy="50%" r="50%">
            <stop offset="0%" stop-color="#a5b4fc" stop-opacity="0.95" />
            <stop offset="60%" stop-color="#6366f1" stop-opacity="0.7" />
            <stop offset="100%" stop-color="#4338ca" stop-opacity="0.1" />
          </radialGradient>
          <filter id="brain-glow" x="-50%" y="-50%" width="200%" height="200%">
            <feGaussianBlur stdDeviation="6" />
          </filter>
          <filter id="brain-glow-sm" x="-50%" y="-50%" width="200%" height="200%">
            <feGaussianBlur stdDeviation="3" />
          </filter>
        </defs>

        <!-- Core halo -->
        <circle cx="0" cy="0" r="70" fill="#818cf8" filter="url(#brain-glow)" opacity="0.18" class="core-halo" />
        <circle cx="0" cy="0" r="42" fill="#818cf8" filter="url(#brain-glow-sm)" opacity="0.28" />

        <!-- Edges from ino to each domain -->
        <g class="edges">
          <line
            v-for="(d, i) in visibleDomains"
            :key="'e-' + d.name"
            x1="0" y1="0"
            :x2="domainX(i)" :y2="domainY(i)"
            class="edge"
            :class="readinessClass(d)"
          />
        </g>

        <!-- Signal pulses on ready edges -->
        <g class="pulses">
          <circle
            v-for="(d, i) in visibleDomains"
            v-show="readinessOf(d) === 'READY'"
            :key="'p-' + d.name"
            r="2.5"
            class="pulse"
          >
            <animateMotion
              :dur="(2.4 + (i % 5) * 0.3).toFixed(1) + 's'"
              :begin="(i * 0.08).toFixed(2) + 's'"
              repeatCount="indefinite"
              :path="`M0,0 L${domainX(i)},${domainY(i)}`"
            />
          </circle>
        </g>

        <!-- Central "ino" node -->
        <g class="ino-core">
          <circle cx="0" cy="0" r="26" fill="url(#brain-core-grad)" class="core-fill" />
          <circle cx="0" cy="0" r="26" class="core-ring" />
          <text x="0" y="5" text-anchor="middle" class="core-label">ino</text>
        </g>

        <!-- Domain nodes + labels -->
        <g class="domains">
          <g
            v-for="(d, i) in visibleDomains"
            :key="d.name"
            class="domain"
            :class="[readinessClass(d), { selected: selectedDomain?.name === d.name }]"
            @click.stop="selectDomain(d)"
          >
            <circle
              :cx="domainX(i)"
              :cy="domainY(i)"
              :r="nodeRadius(d) + 4"
              class="domain-halo"
            />
            <circle
              :cx="domainX(i)"
              :cy="domainY(i)"
              :r="nodeRadius(d)"
              class="domain-node"
            />
            <text
              :x="domainX(i)"
              :y="domainY(i) + nodeRadius(d) + 14"
              text-anchor="middle"
              class="domain-label"
            >
              {{ d.name }}
            </text>
            <text
              :x="domainX(i)"
              :y="domainY(i) + nodeRadius(d) + 26"
              text-anchor="middle"
              class="domain-meta"
            >
              {{ d.apps.length }} · {{ d.readyPct }}%
            </text>
          </g>
        </g>

        <!-- Expanded apps around selected domain -->
        <g v-if="selectedDomain" class="apps">
          <g v-for="(app, ai) in selectedDomain.apps" :key="app.name">
            <line
              :x1="domainX(selectedIndex)"
              :y1="domainY(selectedIndex)"
              :x2="appPos(ai, selectedDomain.apps.length).x"
              :y2="appPos(ai, selectedDomain.apps.length).y"
              class="app-edge"
            />
            <circle
              :cx="appPos(ai, selectedDomain.apps.length).x"
              :cy="appPos(ai, selectedDomain.apps.length).y"
              r="5"
              class="app-node"
            />
            <text
              :x="appPos(ai, selectedDomain.apps.length).x"
              :y="appPos(ai, selectedDomain.apps.length).y - 10"
              text-anchor="middle"
              class="app-label"
            >
              {{ app.name }}
            </text>
          </g>
        </g>
      </svg>
    </div>

    <div class="brain-hint">
      Drag to pan · scroll to zoom · click a domain to expand its apps
    </div>
  </div>
</template>

<style scoped>
.brain-container {
  max-width: 960px;
  margin: 32px auto;
  opacity: 0;
  transform: translateY(14px);
  transition: opacity 0.7s cubic-bezier(0.16, 1, 0.3, 1),
              transform 0.7s cubic-bezier(0.16, 1, 0.3, 1);
}
.brain-container.visible {
  opacity: 1;
  transform: translateY(0);
}

/* ── Toolbar ── */
.brain-toolbar {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 10px 14px;
  margin-bottom: 10px;
  background: var(--vp-c-bg-alt);
  border: 1px solid var(--vp-c-divider);
  border-radius: 12px 12px 0 0;
  flex-wrap: wrap;
}

.brain-stats {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  color: var(--vp-c-text-2);
}
.brain-stats strong {
  color: var(--vp-c-brand-1);
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}
.stat-sep {
  opacity: 0.4;
}

.brain-filters {
  display: flex;
  gap: 6px;
  margin-left: auto;
}

.filter-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 5px 12px;
  font-size: 12px;
  font-weight: 500;
  color: var(--vp-c-text-2);
  background: transparent;
  border: 1px solid var(--vp-c-divider);
  border-radius: 980px;
  cursor: pointer;
  transition: background 0.2s, border-color 0.2s, color 0.2s;
}
.filter-btn:hover {
  color: var(--vp-c-text-1);
  border-color: var(--vp-c-brand-1);
}
.filter-btn.active {
  color: var(--vp-c-brand-1);
  border-color: var(--vp-c-brand-1);
  background: var(--vp-c-brand-soft);
}
.filter-btn .dot {
  display: inline-block;
  width: 8px;
  height: 8px;
  border-radius: 50%;
}
.filter-btn.r-ready .dot   { background: #6ee7b7; box-shadow: 0 0 6px rgba(110, 231, 183, 0.55); }
.filter-btn.r-planned .dot { background: #fcd34d; box-shadow: 0 0 6px rgba(252, 211, 77, 0.5); }
.filter-btn.r-vision .dot  { background: #9ca3af; }

.reset-btn {
  padding: 5px 12px;
  font-size: 12px;
  font-weight: 500;
  color: var(--vp-c-text-2);
  background: transparent;
  border: 1px solid var(--vp-c-divider);
  border-radius: 980px;
  cursor: pointer;
  transition: background 0.2s, color 0.2s, border-color 0.2s;
}
.reset-btn:hover {
  color: var(--vp-c-brand-1);
  border-color: var(--vp-c-brand-1);
  background: var(--vp-c-brand-soft);
}

/* ── Canvas ── */
.brain-canvas {
  position: relative;
  height: 620px;
  background:
    radial-gradient(circle at center, rgba(99, 102, 241, 0.05) 0%, transparent 60%),
    var(--vp-c-bg-alt);
  border: 1px solid var(--vp-c-divider);
  border-top: none;
  border-radius: 0 0 12px 12px;
  overflow: hidden;
  cursor: grab;
  user-select: none;
}
.brain-canvas.panning { cursor: grabbing; }

.brain-svg {
  width: 100%;
  height: 100%;
  display: block;
  transform-origin: center center;
  transition: transform 0.05s linear;
}

/* ── Core ── */
.core-fill {
  /* actual fill is gradient; this keeps class target free */
}
.core-ring {
  fill: none;
  stroke: #a5b4fc;
  stroke-width: 1.2;
  opacity: 0.7;
  animation: brain-core-pulse 3.2s ease-in-out infinite;
}
.core-halo {
  animation: brain-halo-breathe 4s ease-in-out infinite;
}
.core-label {
  fill: #fff;
  font-family: var(--vp-font-family-base);
  font-size: 14px;
  font-weight: 800;
  letter-spacing: -0.02em;
  pointer-events: none;
  dominant-baseline: middle;
}

@keyframes brain-core-pulse {
  0%, 100% { stroke-width: 1.2; opacity: 0.65; }
  50%      { stroke-width: 2;   opacity: 1; }
}
@keyframes brain-halo-breathe {
  0%, 100% { opacity: 0.15; }
  50%      { opacity: 0.3; }
}

/* ── Edges ── */
.edge {
  stroke-width: 1;
  fill: none;
  stroke-dasharray: 4 6;
  transition: stroke 0.3s, opacity 0.3s;
}
.edge.r-ready {
  stroke: rgba(110, 231, 183, 0.55);
  animation: brain-edge-flow 3s linear infinite;
}
.edge.r-planned {
  stroke: rgba(252, 211, 77, 0.42);
}
.edge.r-vision {
  stroke: rgba(156, 163, 175, 0.22);
}

@keyframes brain-edge-flow {
  from { stroke-dashoffset: 0;  }
  to   { stroke-dashoffset: -40; }
}

/* ── Pulses ── */
.pulse {
  fill: #6ee7b7;
  opacity: 0.7;
  filter: drop-shadow(0 0 3px rgba(110, 231, 183, 0.6));
}

/* ── Domain nodes ── */
.domain { cursor: pointer; }

.domain-halo {
  fill: transparent;
  stroke: transparent;
  stroke-width: 2;
  transition: stroke 0.25s, fill 0.25s;
}
.domain:hover .domain-halo,
.domain.selected .domain-halo {
  fill: rgba(129, 140, 248, 0.08);
  stroke: var(--vp-c-brand-1);
}

.domain-node {
  stroke-width: 1.5;
  transition: stroke-width 0.2s, filter 0.25s;
}
.domain.r-ready .domain-node {
  fill: rgba(110, 231, 183, 0.18);
  stroke: #6ee7b7;
}
.domain.r-planned .domain-node {
  fill: rgba(252, 211, 77, 0.15);
  stroke: #fcd34d;
}
.domain.r-vision .domain-node {
  fill: rgba(156, 163, 175, 0.1);
  stroke: #9ca3af;
}
.domain:hover .domain-node,
.domain.selected .domain-node {
  stroke-width: 2.5;
  filter: drop-shadow(0 0 8px currentColor);
}
.domain.r-ready:hover .domain-node,
.domain.r-ready.selected .domain-node   { filter: drop-shadow(0 0 8px rgba(110, 231, 183, 0.8)); }
.domain.r-planned:hover .domain-node,
.domain.r-planned.selected .domain-node { filter: drop-shadow(0 0 8px rgba(252, 211, 77, 0.75)); }
.domain.r-vision:hover .domain-node,
.domain.r-vision.selected .domain-node  { filter: drop-shadow(0 0 8px rgba(165, 180, 252, 0.6)); }

.domain-label {
  fill: var(--vp-c-text-1);
  font-family: var(--vp-font-family-base);
  font-size: 11px;
  font-weight: 600;
  pointer-events: none;
}
.domain-meta {
  fill: var(--vp-c-text-3);
  font-family: var(--vp-font-family-base);
  font-size: 9px;
  font-weight: 500;
  pointer-events: none;
}

/* ── Apps (expanded view) ── */
.apps {
  animation: brain-fade-in 0.35s ease-out;
}
@keyframes brain-fade-in {
  from { opacity: 0; transform: scale(0.94); }
  to   { opacity: 1; transform: scale(1); }
}

.app-edge {
  stroke: rgba(165, 180, 252, 0.35);
  stroke-width: 0.8;
  stroke-dasharray: 2 3;
}
.app-node {
  fill: rgba(165, 180, 252, 0.8);
  stroke: #c7d2fe;
  stroke-width: 1;
  filter: drop-shadow(0 0 4px rgba(129, 140, 248, 0.5));
}
.app-label {
  fill: var(--vp-c-text-2);
  font-family: var(--vp-font-family-base);
  font-size: 9px;
  font-weight: 500;
  pointer-events: none;
}

/* ── Hint ── */
.brain-hint {
  margin-top: 10px;
  text-align: center;
  font-size: 11.5px;
  color: var(--vp-c-text-3);
  font-style: italic;
}

/* ── Responsive ── */
@media (max-width: 720px) {
  .brain-canvas { height: 480px; }
  .brain-toolbar {
    gap: 8px;
    padding: 8px 10px;
  }
  .brain-filters { margin-left: 0; flex-wrap: wrap; }
  .brain-stats { font-size: 11px; }
  .filter-btn, .reset-btn {
    padding: 4px 10px;
    font-size: 11px;
  }
}
</style>
