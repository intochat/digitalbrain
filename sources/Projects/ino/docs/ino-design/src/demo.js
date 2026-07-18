// 6s Tokyo storyboard runner + replan
import { STORY, STORY_REPLAN, ALIASES } from './data.js';
import { fireSynapse, lingerGhost, focusCluster, flareNode } from './brain.js';
import { showCard, morphHotelsDay3, clearCards, bindTrace } from './cards.js';
import { setOrb } from './orb.js';

const clock = document.getElementById('demo-clock');
const stage = document.getElementById('demo-stage');
const utterInput = document.getElementById('utter-input');

let timers = [];
let running = false;
let paused = false;
let pauseAt = 0;
let startedAt = 0;

function clearTimers() { timers.forEach(t => clearTimeout(t)); timers = []; }

export function play(story = STORY, label = 'Tokyo plan · 6s') {
  if (running) stop();
  clearCards();
  running = true; paused = false;
  startedAt = performance.now();
  stage.textContent = '· ' + label;

  const tickerStart = performance.now();
  const ticker = setInterval(() => {
    if (!running || paused) return;
    const t = (performance.now() - startedAt) / 1000;
    clock.textContent = `t = ${t.toFixed(1)}s`;
  }, 60);
  timers.push(ticker);

  story.forEach(ev => {
    const id = setTimeout(() => runEvent(ev), ev.t * 1000);
    timers.push(id);
  });

  // end
  const endT = (story[story.length-1].t + 1) * 1000;
  const id = setTimeout(() => {
    running = false;
    stage.textContent = '— done · brain ghost lingers';
    lingerGhost(['travel','recall','location','reminders']);
  }, endT);
  timers.push(id);
}

function runEvent(ev) {
  if (ev.kind === 'orb') {
    setOrb(ev.state);
    if (ev.state === 'listening') {
      // animate phantom utterance
      typeUtterance(STORY.find(s => s.kind === 'utter')?.text ?? '', 1100);
    }
  } else if (ev.kind === 'utter') {
    // already handled
  } else if (ev.kind === 'syn') {
    fireSynapse({
      from: ev.from, to: ev.to,
      payload: ev.payload,
      gold: ev.color === 'gold',
      color: ev.color === 'gold' ? 'gold' : 'cyan',
      dur: 0.42 + Math.random() * 0.18,
    });
    // focus on receiving cluster
    const target = ALIASES[ev.to];
    if (target) focusCluster(target.cluster);
  } else if (ev.kind === 'card') {
    if (ev.stage === 'enter') showCard(ev.id);
    else if (ev.stage === 'morph') morphHotelsDay3();
  }
}

function typeUtterance(text, dur = 1000) {
  utterInput.value = '';
  const t0 = performance.now();
  const len = text.length;
  function step() {
    const u = Math.min(1, (performance.now() - t0) / dur);
    const i = Math.floor(u * len);
    utterInput.value = text.slice(0, i);
    if (u < 1) requestAnimationFrame(step);
  }
  requestAnimationFrame(step);
}

export function stop() {
  clearTimers();
  running = false;
  paused = false;
  setOrb('idle');
}

export function pause() {
  if (!running) return;
  paused = !paused;
  stage.textContent = paused ? '— paused' : '· resumed';
  // we don't actually pause animation engine in this prototype; kept simple
}

// wire buttons
document.getElementById('btn-play').addEventListener('click', () => play());
document.getElementById('btn-replay').addEventListener('click', () => { stop(); setTimeout(() => play(), 80); });
document.getElementById('btn-pause').addEventListener('click', () => pause());
document.getElementById('btn-replan').addEventListener('click', () => {
  // run the partial replan; only PlanTrip + FindHotels glow
  play(STORY_REPLAN, 'replan · day 3 cheaper');
});

// trace replay (click chevron in card)
bindTrace((cardId) => {
  if (cardId === 'flights') replay([['Cortex','PlanTrip'],['PlanTrip','FindFlights']]);
  else if (cardId === 'hotels') replay([['Cortex','PlanTrip'],['Preferences','PlanTrip','gold'],['PlanTrip','FindHotels']]);
  else if (cardId === 'itinerary') replay([['Forecast','PlanTrip'],['Preferences','PlanTrip','gold'],['PlanTrip','FindPlaces']]);
  else if (cardId === 'reminder') replay([['PlanTrip','VisaReminder']]);
});

function replay(arcs) {
  arcs.forEach((a, i) => setTimeout(() => fireSynapse({ from: a[0], to: a[1], gold: a[2] === 'gold', dur: 0.5 }), i * 280));
}

// fire test from inspector
window.addEventListener('ino-fire-test', (e) => {
  const alias = e.detail.alias;
  const partner = alias === 'Cortex' ? 'PlanTrip' : 'Cortex';
  fireSynapse({ from: alias, to: partner, dur: 0.5, payload: { test: true } });
});

// keyboard
window.addEventListener('keydown', e => {
  if (e.key === ' ' && document.activeElement !== utterInput) { e.preventDefault(); pause(); }
  if (e.key === 'Enter' && document.activeElement === utterInput) {
    e.preventDefault();
    play();
  }
});
