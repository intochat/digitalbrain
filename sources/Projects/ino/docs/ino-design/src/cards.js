// Composition canvas — glass cards stacked on a soft 3D shelf
import { CARDS, HOTELS_REPLAN, CLUSTERS } from './data.js';

const compose = document.getElementById('compose');

// A grid of three columns; reminder card lives below the third
const layout = {
  flights:   { col: 0 },
  hotels:    { col: 1 },
  itinerary: { col: 2 },
  reminder:  { col: 2, row: 1 },
};

const cardEls = {};

function chipFor(text, tone = 'cyan') {
  const map = {
    cyan: ['rgba(61,220,255,0.10)', 'rgba(61,220,255,0.45)', '#9CEAFF'],
    indigo: ['rgba(124,138,255,0.10)', 'rgba(124,138,255,0.45)', '#B6BEFF'],
    gold: ['rgba(232,197,106,0.12)', 'rgba(232,197,106,0.5)', '#F1D88B'],
    muted: ['rgba(255,255,255,0.04)', 'rgba(124,138,255,0.18)', '#7C8AAA'],
    pink: ['rgba(244,184,228,0.10)', 'rgba(244,184,228,0.45)', '#F4B8E4'],
  };
  const [bg, br, c] = map[tone];
  return `<span class="mono text-[10px]" style="padding:2px 7px; border-radius:999px; background:${bg}; border:1px solid ${br}; color:${c};">${text}</span>`;
}

function render(id, content) {
  const card = CARDS[id];
  const el = document.createElement('div');
  el.className = 'ino-card glass-strong';
  el.style.cssText = `
    width: 320px; padding: 16px 18px;
    will-change: transform, opacity;
    box-shadow: 0 30px 80px -30px rgba(8,12,28,0.85), 0 0 0 1px rgba(125,138,255,0.16);
  `;
  // position based on layout — responsive to viewport
  const lay = layout[id];
  const vw = compose.clientWidth || window.innerWidth * 0.78;
  const colW = Math.min(360, vw / 3);
  const startX = -colW; // center column at 0
  const x = startX + lay.col * colW;
  const y = lay.row === 1 ? 220 : 0;
  el.style.width = Math.min(320, colW - 24) + 'px';
  el.style.transform = `translate(${x}px, ${y}px) translateZ(0) scale(0.92)`;
  el.style.opacity = 0;
  el.dataset.cardId = id;
  el.dataset.x = x; el.dataset.y = y;

  // header
  const tone = id === 'reminder' ? 'pink' : 'cyan';
  el.innerHTML = `
    <div class="flex items-start justify-between gap-3 mb-3">
      <div>
        <div class="mono text-[10px] text-[var(--muted-2)] tracking-[0.18em] uppercase mb-1">${card.cluster}</div>
        <div class="text-[15px] font-semibold tracking-[-0.01em] leading-[20px]">${card.title}</div>
        <div class="mono text-[11px] text-[var(--muted)] mt-0.5">${card.sub}</div>
      </div>
      <div class="flex flex-col items-end gap-1">${chipFor('live', tone)}</div>
    </div>
    <div class="space-y-1.5" data-rows></div>
    <div class="mt-3 pt-3 border-t border-[var(--line)] flex items-center justify-between">
      <div class="flex items-center gap-2">
        <button class="mono text-[10px] text-[var(--muted)] hover:text-[var(--text)]" data-pin>◇ pin</button>
        <button class="mono text-[10px] text-[var(--muted)] hover:text-[var(--text)]" data-dismiss>✕ dismiss</button>
      </div>
      <button class="mono text-[10px] text-[var(--muted)] hover:text-[var(--cyan)] flex items-center gap-1" data-trace>
        ← see which neurons made this <span style="font-size:13px;">›</span>
      </button>
    </div>
  `;

  const rowsEl = el.querySelector('[data-rows]');
  renderRows(rowsEl, id, card.rows);

  compose.appendChild(el);
  cardEls[id] = el;
  return el;
}

function renderRows(rowsEl, id, rows) {
  rowsEl.innerHTML = '';
  rows.forEach((r, i) => {
    const row = document.createElement('div');
    row.className = 'flex items-center gap-3 py-1.5 px-2 rounded-md';
    row.style.background = i % 2 ? 'rgba(255,255,255,0.015)' : 'transparent';
    if (r.dim) row.style.opacity = 0.42;
    if (r.highlight) {
      row.style.background = 'linear-gradient(90deg, rgba(232,197,106,0.10), rgba(232,197,106,0.02))';
      row.style.borderLeft = '2px solid rgba(232,197,106,0.6)';
    }
    let html = '';
    if (id === 'flights') {
      html = `
        <span class="mono text-[11px] text-[var(--muted)]" style="width:54px;">${r.code}</span>
        <span class="mono text-[12px] flex-1 truncate">${r.route}</span>
        <span class="mono text-[11px] text-[var(--muted)]" style="width:64px; text-align:right;">${r.dur}</span>
        <span class="mono text-[12px]" style="width:54px; text-align:right;">${r.price}</span>
      `;
    } else if (id === 'hotels') {
      html = `
        <span class="text-[12px] font-medium flex-1 truncate" style="line-height:14px;">${r.name}<br/><span class="mono text-[10px] text-[var(--muted-2)]">${r.area} · ${r.note}</span></span>
        <span class="mono text-[12px]" style="width:64px; text-align:right;">${r.price}</span>
        <span style="width:160px; text-align:right;">${chipFor(r.tag, r.highlight ? 'gold' : (r.dim ? 'muted' : 'indigo'))}</span>
      `;
    } else if (id === 'itinerary') {
      const wxNum = parseInt(r.wx);
      const wxTone = wxNum >= 60 ? 'gold' : 'cyan';
      html = `
        <span class="mono text-[11px]" style="width:48px; color:var(--text);">${r.day}</span>
        <span style="width:48px;">${chipFor(r.wx + ' rain', wxTone)}</span>
        <span class="text-[12px] flex-1 truncate" style="${r.highlight ? 'color:var(--text); font-weight:500;' : 'color:var(--muted);'}">${r.plan}</span>
      `;
    } else if (id === 'reminder') {
      html = `
        <span class="dot gold"></span>
        <span class="text-[12px] flex-1">${r.name}</span>
        <span class="mono text-[11px] text-[var(--muted)]">${r.when}</span>
        <span>${chipFor(r.tag, 'gold')}</span>
      `;
    }
    row.innerHTML = html;
    rowsEl.appendChild(row);
  });
}

export function showCard(id) {
  let el = cardEls[id] || render(id, CARDS[id]);
  document.body.classList.add('cards-active');
  // spring entrance — explicitly set final state to override inline opacity:0
  const fromY = parseFloat(el.dataset.y) + 30;
  el.animate(
    [
      { transform: `translate(${el.dataset.x}px, ${fromY}px) scale(0.9)`, opacity: 0 },
      { transform: `translate(${el.dataset.x}px, ${el.dataset.y}px) scale(1.02)`, opacity: 1, offset: 0.7 },
      { transform: `translate(${el.dataset.x}px, ${el.dataset.y}px) scale(1)`, opacity: 1 },
    ],
    { duration: 540, easing: 'cubic-bezier(0.22, 1, 0.36, 1)', fill: 'forwards' }
  );
  // commit final state on inline styles so it persists past animation
  setTimeout(() => {
    el.style.opacity = 1;
    el.style.transform = `translate(${el.dataset.x}px, ${el.dataset.y}px) scale(1)`;
  }, 560);
  return el;
}

export function morphHotelsDay3() {
  const el = cardEls.hotels;
  if (!el) return;
  const rowsEl = el.querySelector('[data-rows]');
  // crossfade rows
  rowsEl.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 220, easing: 'ease-out', fill: 'forwards' }).onfinish = () => {
    renderRows(rowsEl, 'hotels', HOTELS_REPLAN.rows);
    rowsEl.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 320, easing: 'ease-out', fill: 'forwards' });
    el.classList.add('swap-flash');
    setTimeout(() => el.classList.remove('swap-flash'), 1200);
  };
}

export function clearCards() {
  Object.values(cardEls).forEach(el => el.remove());
  for (const k in cardEls) delete cardEls[k];
  document.body.classList.remove('cards-active');
}

// trace replay — when user clicks "see which neurons made this"
export function bindTrace(handler) {
  compose.addEventListener('click', e => {
    const t = e.target.closest('[data-trace]');
    if (!t) return;
    const card = t.closest('.ino-card');
    handler(card?.dataset.cardId);
  });
  compose.addEventListener('click', e => {
    const t = e.target.closest('[data-dismiss]');
    if (!t) return;
    const card = t.closest('.ino-card');
    card.animate([{ opacity: 1 }, { opacity: 0, transform: card.style.transform + ' translateY(20px)' }], { duration: 240, fill: 'forwards' }).onfinish = () => card.remove();
  });
}
