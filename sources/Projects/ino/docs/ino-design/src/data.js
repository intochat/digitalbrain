// mocked ino state — every chunk labeled // mocked
// All clusters live on a sphere, organized by domain.

export const CLUSTERS = /* mocked */ [
  { id: 'cortex',    label: 'CORTEX',    domain: 'system',    pos: [  0,  0.20,  1.05], color: 0xE6EDF7, hue: 0.62, count: 1, size: 0.34 },
  { id: 'travel',    label: 'TRAVEL',    domain: 'travel',    pos: [ 0.95,  0.55,  0.10], color: 0x7C8AFF, hue: 0.65, count: 9, size: 0.28 },
  { id: 'recall',    label: 'RECALL',    domain: 'recall',    pos: [-0.85,  0.65,  0.40], color: 0xE8C56A, hue: 0.12, count: 7, size: 0.26 },
  { id: 'location',  label: 'LOCATION',  domain: 'location',  pos: [ 0.60, -0.65,  0.55], color: 0x6EE7A8, hue: 0.42, count: 4, size: 0.22 },
  { id: 'reminders', label: 'REMINDERS', domain: 'reminders', pos: [-0.55, -0.55,  0.70], color: 0xF4B8E4, hue: 0.92, count: 5, size: 0.20 },
  { id: 'taxi',      label: 'TAXI',      domain: 'taxi',      pos: [ 0.25, -0.95, -0.30], color: 0xFFD08A, hue: 0.10, count: 4, size: 0.18 },
  { id: 'genesis',   label: 'GENESIS',   domain: 'genesis',   pos: [-1.00,  0.05, -0.20], color: 0xC9D6FF, hue: 0.66, count: 6, size: 0.20 },
  { id: 'identity',  label: 'IDENTITY',  domain: 'identity',  pos: [ 0.05,  0.95, -0.45], color: 0xB8C5E0, hue: 0.62, count: 3, size: 0.18 },
];

const ALIAS = {
  cortex:    ['Cortex'],
  travel:    ['PlanTrip', 'FindFlights', 'FindHotels', 'FindPlaces', 'BookFlight', 'BookHotel', 'TripBudget', 'WeatherFit', 'Itinerary'],
  recall:    ['Preferences', 'PriorTrips', 'PeopleGraph', 'StyleBias', 'Pinned', 'Episodes', 'Aliases'],
  location:  ['Forecast', 'GeoIndex', 'TimeZone', 'Heatmap'],
  reminders: ['VisaReminder', 'Schedule', 'Followups', 'Snooze', 'Pulse'],
  taxi:      ['Hail', 'Surge', 'Driver', 'Eta'],
  genesis:   ['L1Forge', 'L2Sketch', 'L3Review', 'Sandbox', 'Schema', 'Catalog'],
  identity:  ['Self', 'Persona', 'Tenancy'],
};

export const NEURONS = /* mocked — 41 nodes */ (() => {
  const out = [];
  let nid = 0;
  for (const c of CLUSTERS) {
    for (let i = 0; i < c.count; i++) {
      out.push({
        id: `n${nid++}`,
        cluster: c.id,
        alias: ALIAS[c.id]?.[i] ?? `${c.id}.n${i}`,
        domain: c.domain,
        decay: 60 + Math.floor(Math.random() * 35),
        color: c.color,
      });
    }
  }
  return out;
})();

export const ALIASES = Object.fromEntries(NEURONS.map(n => [n.alias, n]));

// Storyboard: timestamped synapse events for the 6s Tokyo plan
export const STORY = /* mocked — Tokyo plan, 6s */ [
  { t: 0.00, kind: 'orb',    state: 'listening' },
  { t: 0.00, kind: 'utter',  text: 'Plan a 5-day Tokyo trip in late October, rain-friendly, mid-budget, leave from Kyiv.' },
  { t: 1.20, kind: 'orb',    state: 'thinking' },
  { t: 1.20, kind: 'syn',    from: 'Cortex',     to: 'PlanTrip',     payload: { intent: 'plan_trip', city: 'Tokyo' } },
  { t: 1.60, kind: 'syn',    from: 'PlanTrip',   to: 'FindFlights',  payload: { from: 'KBP', to: 'NRT', when: '2026-10-22..27', tier: 'mid' } },
  { t: 1.62, kind: 'syn',    from: 'PlanTrip',   to: 'FindHotels',   payload: { city: 'Tokyo', tier: 'mid', constraints: ['rain-friendly'] } },
  { t: 1.64, kind: 'syn',    from: 'PlanTrip',   to: 'FindPlaces',   payload: { city: 'Tokyo', mood: 'rain-friendly' } },
  { t: 2.00, kind: 'syn',    from: 'Preferences', to: 'PlanTrip',    color: 'gold', payload: { ryokanBias: 0.62, hotelChainBias: -0.38, source: 'recall.priorTrips' } },
  { t: 2.40, kind: 'syn',    from: 'Forecast',    to: 'PlanTrip',    payload: { tokyo_oct: { d1: 0.22, d2: 0.61, d3: 0.78, d4: 0.30, d5: 0.18 } } },
  { t: 3.00, kind: 'card',   id: 'flights',     stage: 'enter', from: 'travel' },
  { t: 3.80, kind: 'card',   id: 'hotels',      stage: 'enter', from: 'travel' },
  { t: 4.60, kind: 'card',   id: 'itinerary',   stage: 'enter', from: 'travel' },
  { t: 5.40, kind: 'syn',    from: 'PlanTrip',  to: 'VisaReminder', payload: { topic: 'visa', remindIn: '3 days' } },
  { t: 5.50, kind: 'card',   id: 'reminder',    stage: 'enter', from: 'reminders' },
  { t: 6.00, kind: 'orb',    state: 'celebrating' },
  { t: 6.20, kind: 'orb',    state: 'idle' },
];

// "make day 3 cheaper" follow-up — only PlanTrip + FindHotels
export const STORY_REPLAN = /* mocked */ [
  { t: 0.00, kind: 'utter',  text: 'Make day 3 cheaper.' },
  { t: 0.10, kind: 'orb',    state: 'thinking' },
  { t: 0.30, kind: 'syn',    from: 'Cortex',    to: 'PlanTrip',   payload: { intent: 'refine', dim: 'day3.budget' } },
  { t: 0.55, kind: 'syn',    from: 'PlanTrip',  to: 'FindHotels', payload: { day: 3, max: 'mid-low', swap: true } },
  { t: 1.20, kind: 'card',   id: 'hotels',      stage: 'morph' },
  { t: 1.40, kind: 'orb',    state: 'idle' },
];

// Cards content (Tokyo)
export const CARDS = /* mocked */ {
  flights: {
    title: 'Flights · Kyiv → Tokyo',
    sub: 'mid-budget · 3 candidates',
    cluster: 'travel',
    rows: [
      { code: 'TK 762',  route: 'KBP → IST → NRT', dur: '15h 25m', price: '$612', tag: 'best value' },
      { code: 'LO 8071', route: 'KBP → WAW → HND', dur: '14h 50m', price: '$695', tag: 'shortest' },
      { code: 'QR 5113', route: 'KBP → DOH → HND', dur: '17h 40m', price: '$574', tag: 'cheapest' },
    ],
  },
  hotels: {
    title: 'Stays · Tokyo, late Oct',
    sub: 'rain-friendly · onsen access prioritized',
    cluster: 'travel',
    rows: [
      { name: 'Hoshinoya Tokyo',   area: 'Otemachi',    note: 'urban ryokan · onsen',         price: '$240/n', tag: 'recall: ryokan +0.62' },
      { name: 'Andon Ryokan',      area: 'Asakusa',     note: 'classic · indoor baths',       price: '$95/n',  tag: 'value' },
      { name: 'Trunk House',       area: 'Kagurazaka',  note: 'private · rainy-day cozy',     price: '$310/n', tag: 'splurge' },
      { name: 'Mimaru Akasaka',    area: 'Akasaka',     note: 'chain · skipped',              price: '—',      tag: 'dimmed', dim: true },
    ],
  },
  itinerary: {
    title: 'Itinerary · 5 days, weather-fit',
    sub: 'indoor anchors mapped to rain peaks',
    cluster: 'travel',
    rows: [
      { day: 'Day 1', wx: '22%', plan: 'Arrive HND · Asakusa walk · Senso-ji at dusk' },
      { day: 'Day 2', wx: '61%', plan: 'TeamLab Borderless · Shimokitazawa cafés' },
      { day: 'Day 3', wx: '78%', plan: 'TeamLab Planets (rain anchor) · onsen evening', highlight: true },
      { day: 'Day 4', wx: '30%', plan: 'Shibuya · Harajuku · Yoyogi park' },
      { day: 'Day 5', wx: '18%', plan: 'Tsukiji breakfast · depart NRT' },
    ],
  },
  reminder: {
    title: 'Reminder · pre-trip',
    sub: 'reminders · soft',
    cluster: 'reminders',
    rows: [{ name: 'Check visa requirements', when: 'in 3 days', tag: 'auto · accept?' }],
  },
};

// Day-3 morph after replan
export const HOTELS_REPLAN = /* mocked */ {
  rows: [
    { name: 'Hoshinoya Tokyo',  area: 'Otemachi',    note: 'urban ryokan · onsen',  price: '$240/n', tag: 'recall: ryokan +0.62' },
    { name: 'Sakura Ryokan',    area: 'Iriya',       note: 'family-run · onsen',    price: '$78/n',  tag: 'swap · day 3 only', highlight: true },
    { name: 'Andon Ryokan',     area: 'Asakusa',     note: 'classic · indoor baths',price: '$95/n',  tag: 'kept' },
    { name: 'Trunk House',      area: 'Kagurazaka',  note: 'private · rainy-day',   price: '$310/n', tag: 'dimmed', dim: true },
  ],
};

// Density river — minute buckets across last 24h, plus a long tail for "this week"
export const DENSITY = /* mocked */ (() => {
  const out = [];
  const N = 280;
  for (let i = 0; i < N; i++) {
    const t = i / N;
    // base diurnal sine + bursts
    let v = 0.18 + 0.32 * Math.sin(t * Math.PI * 2.6 - 0.6);
    v += 0.12 * Math.sin(t * Math.PI * 11);
    v += Math.random() * 0.08;
    if (i > N - 26) v += 0.45 * Math.exp(-(N - i) / 8); // recent burst
    out.push(Math.max(0.02, Math.min(1, v)));
  }
  return out;
})();

export const TIMELINE_MARKS = /* mocked */ [
  { x: 0.06, kind: 'origin', label: 'Origin · ino booted' },
  { x: 0.18, kind: 'green',  label: 'L1 born · TripBudget' },
  { x: 0.31, kind: 'green',  label: 'L1 born · WeatherFit' },
  { x: 0.42, kind: 'gold',   label: 'pinned · "the Bali plan"' },
  { x: 0.55, kind: 'red',    label: 'incident · FindFlights timeout' },
  { x: 0.71, kind: 'gold',   label: 'pinned · "ryokan epiphany"' },
  { x: 0.86, kind: 'green',  label: 'L1 born · VisaReminder' },
  { x: 0.94, kind: 'now',    label: 'now' },
];

// Drawer event lists — keyed by neuron alias
export const DRAWER_EVENTS = /* mocked */ {
  PlanTrip: [
    { t: 'now',   from: 'Cortex',     payload: 'plan_trip{Tokyo}' },
    { t: '+0.4s', from: 'Preferences',payload: 'ryokanBias=0.62',  recall: true },
    { t: '+0.8s', from: 'Forecast',   payload: 'rain[d3]=0.78' },
    { t: '+1.4s', to:   'FindFlights',payload: 'KBP→NRT mid' },
    { t: '+1.4s', to:   'FindHotels', payload: 'Tokyo rain-fit' },
    { t: '+1.4s', to:   'FindPlaces', payload: 'mood=rain' },
    { t: '+4.2s', to:   'VisaReminder', payload: '3d ahead' },
    { t: '−2m',   from: 'Cortex',     payload: 'refresh{cache}' },
    { t: '−14m',  to:   'TripBudget', payload: 'tier=mid' },
    { t: '−1h',   from: 'PriorTrips', payload: 'Bali ref',         recall: true },
  ],
  FindHotels: [
    { t: 'now',   from: 'PlanTrip',   payload: 'rain-fit Tokyo' },
    { t: '+1.1s', to:   'PlanTrip',   payload: '4 candidates' },
    { t: '−5m',  from: 'StyleBias',   payload: 'onsen+0.41', recall: true },
  ],
  Cortex: [
    { t: 'now', from: 'utterance', payload: '"Plan a 5-day Tokyo…"' },
    { t: '+0.0s', to: 'PlanTrip', payload: 'route → travel' },
    { t: '−1m', from: 'utterance', payload: '"what time is it in Tokyo"' },
    { t: '−1m', to: 'TimeZone', payload: 'Asia/Tokyo' },
  ],
};

export function eventsFor(alias) {
  return DRAWER_EVENTS[alias] ?? [
    { t: 'now', from: '—', payload: 'no traffic yet — interact to populate' },
  ];
}
