// Inspector drawer + synapse tooltip
import { eventsFor, ALIASES } from './data.js';
import { projectVec3 } from './brain.js';

const drawer = document.getElementById('drawer');
const tooltip = document.getElementById('syn-tooltip');

const elAlias = document.getElementById('drw-alias');
const elDom = document.getElementById('drw-domain');
const elId = document.getElementById('drw-id');
const elDot = document.getElementById('drw-dot');
const elEvents = document.getElementById('drw-events');
const elCount = document.getElementById('drw-count');
const elPrompt = document.getElementById('drw-prompt');

document.getElementById('drw-close').addEventListener('click', () => drawer.classList.remove('open'));

function renderEvents(alias) {
  const evs = eventsFor(alias);
  elEvents.innerHTML = '';
  evs.forEach(ev => {
    const li = document.createElement('li');
    li.className = 'flex items-center gap-2 py-1 px-2 mono text-[11px]';
    li.style.cssText = 'background: rgba(255,255,255,0.015); border-radius:8px; border:1px solid var(--line);';
    const dir = ev.from ? '←' : '→';
    const peer = ev.from ?? ev.to;
    const tone = ev.recall ? '#E8C56A' : '#7C8AFF';
    li.innerHTML = `
      <span style="color:var(--muted-2); width:38px;">${ev.t}</span>
      <span style="color:${tone};">${dir}</span>
      <span style="width:90px; color:var(--text);" class="truncate">${peer}</span>
      <span style="color:var(--muted);" class="truncate flex-1">${ev.payload}</span>
    `;
    elEvents.appendChild(li);
  });
  elCount.textContent = `${evs.length} events`;
}

function renderSpark(alias) {
  const spark = document.getElementById('spark');
  // mocked sparkline
  const N = 28;
  const pts = [];
  for (let i = 0; i < N; i++) {
    const v = 0.3 + 0.4 * Math.sin(i * 0.7 + alias.length) + Math.random() * 0.18;
    pts.push([i / (N-1) * 280, 56 - v * 56]);
  }
  // remove existing
  spark.querySelectorAll('.spark-line, .spark-fill').forEach(n => n.remove());
  const fill = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  fill.setAttribute('class', 'spark-fill');
  fill.setAttribute('d', `M0,56 L${pts.map(p => p.join(',')).join(' L')} L280,56 Z`);
  spark.appendChild(fill);
  const line = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  line.setAttribute('class', 'spark-line');
  line.setAttribute('d', `M${pts.map(p => p.join(',')).join(' L')}`);
  spark.appendChild(line);
}

const PROMPTS = {
  PlanTrip: `You are PlanTrip. Compose a multi-day itinerary that respects:
  - budget tier ∈ {low, mid, high}
  - weather constraints (rain-friendly = prefer indoor anchors)
  - recall.preferences (ryokan > hotel chain · bias 0.62)
Output: typed synapse PlanComposed { flights, stays, days[] }.`,
  FindHotels: `You are FindHotels. Rank stays in {city} by:
  rain-friendly { onsen, indoor baths, walkable cover } + recall.style.`,
  Cortex: `You route natural language to one typed synapse from IDiscovery catalog.
No decomposition. No self-creation. Unrouteable → UnroutedIntent.`,
  Forecast: `You are a pure-code neuron (no LLM). 7-day forecast lookup with cache.`,
  Preferences: `You are Recall.Preferences. Project pinned moments + decisions into bias scalars.`,
  VisaReminder: `Schedule a reminder synapse N days ahead of a trip departure.`,
};

export function openInspector(neuron) {
  elAlias.textContent = neuron.alias;
  elDom.textContent = neuron.domain;
  elId.textContent = `grain://${neuron.domain}/${neuron.alias.toLowerCase()}/0x${(Math.random()*0xffff|0).toString(16)}`;
  // dot color
  elDot.style.background = '#' + neuron.color.toString(16).padStart(6, '0');
  elDot.style.boxShadow = `0 0 8px #${neuron.color.toString(16).padStart(6, '0')}`;
  renderEvents(neuron.alias);
  renderSpark(neuron.alias);
  elPrompt.textContent = PROMPTS[neuron.alias] ?? `// ${neuron.alias} — pure-code neuron · no LLM. Implementation in domains/${neuron.domain}/...`;
  drawer.classList.add('open');
}

window.addEventListener('ino-neuron-click', e => openInspector(e.detail.neuron));

// Synapse mid-flight tooltip
window.addEventListener('ino-synapse-click', e => {
  const { syn, screenX, screenY } = e.detail;
  document.getElementById('syn-meta').innerHTML =
    `<span style="color:var(--text);">${syn.from}</span> <span style="color:var(--muted-2);">→</span> <span style="color:var(--text);">${syn.to}</span>` +
    `<br/><span style="color:var(--muted-2);">traceparent: 00-${Math.random().toString(16).slice(2,18).padEnd(16,'0')}-${Math.random().toString(16).slice(2,8)}-01</span>` +
    `<br/><span style="color:${syn.gold ? '#E8C56A' : '#B6BEFF'};">decay: ${(60 + Math.random()*30 | 0)} · ${syn.gold ? 'recall' : 'compute'}</span>` +
    `<br/><span style="color:var(--muted-2);">click to resume</span>`;
  document.getElementById('syn-payload').textContent = JSON.stringify(syn.payload, null, 2);
  tooltip.style.left = screenX + 'px';
  tooltip.style.top = screenY + 'px';
  tooltip.classList.add('show');
  // resume on next click anywhere
  const dismiss = (ev) => {
    tooltip.classList.remove('show');
    syn.paused = false;
    window.removeEventListener('click', dismiss, true);
  };
  setTimeout(() => window.addEventListener('click', dismiss, true), 50);
});

// "Fire test synapse"
document.getElementById('drw-fire').addEventListener('click', () => {
  const alias = elAlias.textContent;
  // self-fire for visual feedback
  window.dispatchEvent(new CustomEvent('ino-fire-test', { detail: { alias } }));
});

// Tokens panel
const tokensPanel = document.getElementById('tokens-panel');
document.getElementById('btn-tokens').addEventListener('click', () => tokensPanel.classList.toggle('open'));
document.getElementById('tok-close').addEventListener('click', () => tokensPanel.classList.remove('open'));
