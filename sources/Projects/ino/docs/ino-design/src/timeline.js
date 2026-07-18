// Timeline density river + scrubber + marks
import { DENSITY, TIMELINE_MARKS } from './data.js';

const svg = document.getElementById('density');
const scrubber = document.getElementById('scrubber');
const readout = document.getElementById('time-readout');
const ns = 'http://www.w3.org/2000/svg';

const W = 1280, H = 96;
svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
svg.setAttribute('preserveAspectRatio', 'none');

// Build the river path (mirrored band — peaks both sides)
function pathFor(values) {
  const N = values.length;
  const stepX = W / (N - 1);
  let topPts = '';
  let botPts = '';
  for (let i = 0; i < N; i++) {
    const x = (i * stepX).toFixed(1);
    const v = values[i];
    const yT = ((1 - v) * (H * 0.5)).toFixed(1);
    const yB = (H - yT).toFixed(1);
    topPts += `${x},${yT} `;
    botPts = `${x},${yB} ` + botPts;
  }
  return `M0,${H*0.5} L${topPts} L${botPts} Z`;
}

const river = document.createElementNS(ns, 'path');
river.setAttribute('d', pathFor(DENSITY));
river.setAttribute('class', 'density-bar');
svg.appendChild(river);

// Add a centerline
const center = document.createElementNS(ns, 'line');
center.setAttribute('x1', 0); center.setAttribute('x2', W);
center.setAttribute('y1', H/2); center.setAttribute('y2', H/2);
center.setAttribute('stroke', 'rgba(125,138,255,0.18)');
center.setAttribute('stroke-width', '1');
svg.appendChild(center);

// Marks
TIMELINE_MARKS.forEach(m => {
  if (m.kind === 'now') return; // already at scrubber
  const x = m.x * W;
  const c = document.createElementNS(ns, 'circle');
  c.setAttribute('cx', x); c.setAttribute('cy', H/2);
  c.setAttribute('r', m.kind === 'origin' ? 4 : 3);
  const fill = m.kind === 'gold' ? '#E8C56A'
             : m.kind === 'green' ? '#6EE7A8'
             : m.kind === 'red'   ? '#FF6B6B'
             : m.kind === 'origin' ? '#C9D6FF'
             : '#7C8AFF';
  c.setAttribute('fill', fill);
  c.setAttribute('filter', 'drop-shadow(0 0 6px ' + fill + ')');
  c.style.cursor = 'pointer';

  c.addEventListener('mouseenter', () => showMarkTip(m, x));
  c.addEventListener('mouseleave', hideMarkTip);
  svg.appendChild(c);
});

// fork visualization — at one mark, draw a short fork stub
const fork = document.createElementNS(ns, 'path');
fork.setAttribute('d', `M${0.31*W},${H/2} q12,-12 22,-22 m-22,22 q12,12 22,22`);
fork.setAttribute('stroke', 'rgba(232,197,106,0.55)');
fork.setAttribute('stroke-width', '1.25');
fork.setAttribute('fill', 'none');
fork.setAttribute('stroke-linecap', 'round');
svg.appendChild(fork);

const forkLabel = document.createElementNS(ns, 'text');
forkLabel.setAttribute('x', 0.31*W + 26);
forkLabel.setAttribute('y', H/2 - 22);
forkLabel.setAttribute('fill', 'rgba(232,197,106,0.7)');
forkLabel.setAttribute('font-size', '9');
forkLabel.setAttribute('font-family', 'JetBrains Mono, monospace');
forkLabel.textContent = 'fork · L1 · WeatherFit';
svg.appendChild(forkLabel);

// Mark tooltip
let markTip = null;
function showMarkTip(m, x) {
  hideMarkTip();
  markTip = document.createElement('div');
  markTip.className = 'glass-strong absolute mono';
  markTip.style.cssText = `
    bottom: 110px; left: ${(x / W) * 100}%; transform: translateX(-50%);
    z-index: 30; padding: 8px 10px; font-size: 11px;
    color: var(--text); white-space: nowrap; pointer-events: none;
  `;
  markTip.innerHTML = `<div style="color:var(--muted-2); font-size:9px; letter-spacing:0.18em; text-transform:uppercase; margin-bottom:2px;">${m.kind}</div>${m.label}`;
  document.getElementById('timeline').appendChild(markTip);
}
function hideMarkTip() { if (markTip) { markTip.remove(); markTip = null; } }

// Scrubber drag
let dragging = false;
scrubber.addEventListener('pointerdown', e => { dragging = true; });
window.addEventListener('pointerup', () => { dragging = false; });
window.addEventListener('pointermove', e => {
  if (!dragging) return;
  const tlRect = svg.getBoundingClientRect();
  const x = Math.max(0, Math.min(1, (e.clientX - tlRect.left) / tlRect.width));
  setScrubber(x);
  rewind(x);
});

export function setScrubber(x) {
  scrubber.style.left = `${x * 100}%`;
  // readout
  if (x >= 0.92) readout.textContent = '2026-05-07 · 14:32 · now';
  else if (x >= 0.7) readout.textContent = '2026-05-07 · earlier · scrubbing';
  else if (x >= 0.4) readout.textContent = '2026-05-04 · this week';
  else if (x >= 0.15) readout.textContent = '2026-04-15 · birth of WeatherFit';
  else readout.textContent = '2026-03-21 · origin · ino booted';
}

// Rewind effect (fades brain a bit, dims newer cards)
function rewind(x) {
  const dim = 1 - x;
  document.body.style.setProperty('--rewind', dim.toFixed(2));
  // adjust brain opacity slightly
  const canvas = document.getElementById('brain-canvas');
  canvas.style.filter = `saturate(${0.4 + x*0.6}) blur(${(1-x)*1.2}px)`;
  // hide cards if scrubbed
  const compose = document.getElementById('compose');
  compose.style.opacity = x < 0.85 ? Math.max(0.05, x - 0.05) : 1;
}

// Quick-jump chips
document.querySelectorAll('#time-chips .chip').forEach(b => {
  b.addEventListener('click', () => {
    document.querySelectorAll('#time-chips .chip').forEach(x => x.classList.remove('active'));
    b.classList.add('active');
    const map = { now: 0.92, '10m': 0.86, today: 0.62, week: 0.42, origin: 0.06 };
    const x = map[b.dataset.jump] ?? 0.92;
    // animate
    const start = parseFloat(scrubber.style.left) / 100 || 0.92;
    const t0 = performance.now();
    const dur = 600;
    function step(now) {
      const u = Math.min(1, (now - t0) / dur);
      const e = 1 - Math.pow(1 - u, 3);
      const v = start + (x - start) * e;
      setScrubber(v); rewind(v);
      if (u < 1) requestAnimationFrame(step);
    }
    requestAnimationFrame(step);
  });
});

// Pin-moment
document.getElementById('btn-pin').addEventListener('click', () => {
  const x = parseFloat(scrubber.style.left) / 100 || 0.92;
  const c = document.createElementNS(ns, 'circle');
  c.setAttribute('cx', x * W); c.setAttribute('cy', H/2);
  c.setAttribute('r', 4);
  c.setAttribute('fill', '#E8C56A');
  c.setAttribute('filter', 'drop-shadow(0 0 8px #E8C56A)');
  svg.appendChild(c);
  const ping = c.cloneNode();
  ping.setAttribute('r', 4);
  ping.setAttribute('fill', 'none');
  ping.setAttribute('stroke', '#E8C56A');
  svg.appendChild(ping);
  ping.animate([{ r: 4, opacity: 0.9 }, { r: 22, opacity: 0 }], { duration: 700, easing: 'cubic-bezier(0.22,1,0.36,1)' });
  setTimeout(() => ping.remove(), 720);
});

// init scrubber pos
setScrubber(0.92);
