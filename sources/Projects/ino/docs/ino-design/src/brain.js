// Three.js neuron constellation + synapse comets
import * as THREE from 'three';
import { CLUSTERS, NEURONS, ALIASES } from './data.js';

const canvas = document.getElementById('brain-canvas');
const overlay = document.getElementById('brain-overlay');
const labelLayer = document.getElementById('cluster-labels');

const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(45, 1, 0.01, 100);
camera.position.set(0, 0.2, 4.5);

const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.setClearColor(0x000000, 0);

function resize() {
  const w = window.innerWidth;
  const h = window.innerHeight;
  renderer.setSize(w, h, true);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
  overlay.setAttribute('viewBox', `0 0 ${w} ${h}`);
  overlay.setAttribute('width', w); overlay.setAttribute('height', h);
}
window.addEventListener('resize', resize); resize();

// Neuron node positions: small offsets around cluster center
const nodeGroup = new THREE.Group();
scene.add(nodeGroup);

const haloGroup = new THREE.Group();
scene.add(haloGroup);

const SPHERE_R = 1.55;

// Build nodes
const nodeMeshes = [];
NEURONS.forEach((n, i) => {
  const c = CLUSTERS.find(c => c.id === n.cluster);
  // jitter
  const jitter = new THREE.Vector3(
    (Math.random() - 0.5) * 0.45,
    (Math.random() - 0.5) * 0.45,
    (Math.random() - 0.5) * 0.45,
  );
  const center = new THREE.Vector3(...c.pos).normalize().multiplyScalar(SPHERE_R);
  const pos = center.clone().add(jitter);
  // re-project onto sphere shell
  pos.normalize().multiplyScalar(SPHERE_R + (Math.random() - 0.5) * 0.18);

  const sz = 0.03 + Math.random() * 0.025;
  const geo = new THREE.SphereGeometry(sz, 18, 18);
  const mat = new THREE.MeshBasicMaterial({
    color: c.color,
    transparent: true,
    opacity: 0.85,
  });
  const mesh = new THREE.Mesh(geo, mat);
  mesh.position.copy(pos);
  mesh.userData = { neuron: n, cluster: c, basePos: pos.clone(), baseSize: sz, baseOpacity: 0.78, flare: 0 };
  nodeGroup.add(mesh);
  nodeMeshes.push(mesh);

  // halo
  const haloGeo = new THREE.SphereGeometry(sz * 3.2, 18, 18);
  const haloMat = new THREE.MeshBasicMaterial({
    color: c.color, transparent: true, opacity: 0.0, depthWrite: false, blending: THREE.AdditiveBlending,
  });
  const halo = new THREE.Mesh(haloGeo, haloMat);
  halo.position.copy(pos);
  halo.userData = { mesh, baseSize: sz * 3.2 };
  haloGroup.add(halo);
  mesh.userData.halo = halo;
});

// cluster soft glow (a faint sprite at each cluster center)
const clusterGlowGroup = new THREE.Group();
scene.add(clusterGlowGroup);
CLUSTERS.forEach(c => {
  const center = new THREE.Vector3(...c.pos).normalize().multiplyScalar(SPHERE_R * 0.95);
  const geo = new THREE.SphereGeometry(c.size * 1.4, 24, 24);
  const mat = new THREE.MeshBasicMaterial({ color: c.color, transparent: true, opacity: 0.05, blending: THREE.AdditiveBlending, depthWrite: false });
  const mesh = new THREE.Mesh(geo, mat);
  mesh.position.copy(center);
  mesh.userData = { cluster: c, baseOpacity: 0.05, fire: 0 };
  clusterGlowGroup.add(mesh);
});

// Inter-cluster faint filament lines
const filamentGroup = new THREE.Group();
scene.add(filamentGroup);
const filamentPairs = [
  ['cortex','travel'], ['cortex','recall'], ['cortex','location'], ['cortex','reminders'],
  ['cortex','taxi'], ['cortex','genesis'], ['cortex','identity'],
  ['travel','recall'], ['travel','location'], ['travel','reminders'],
  ['recall','identity'],
];
filamentPairs.forEach(([a,b]) => {
  const ca = CLUSTERS.find(c => c.id === a);
  const cb = CLUSTERS.find(c => c.id === b);
  const pa = new THREE.Vector3(...ca.pos).normalize().multiplyScalar(SPHERE_R * 0.95);
  const pb = new THREE.Vector3(...cb.pos).normalize().multiplyScalar(SPHERE_R * 0.95);
  // bezier
  const mid = pa.clone().add(pb).multiplyScalar(0.5);
  mid.multiplyScalar(0.6);
  const curve = new THREE.QuadraticBezierCurve3(pa, mid, pb);
  const pts = curve.getPoints(40);
  const geo = new THREE.BufferGeometry().setFromPoints(pts);
  const mat = new THREE.LineBasicMaterial({ color: 0x7C8AFF, transparent: true, opacity: 0.05 });
  const line = new THREE.Line(geo, mat);
  line.userData = { baseOpacity: 0.05 };
  filamentGroup.add(line);
});

// Synapse comets — short particle line + bright head
const synapseGroup = new THREE.Group();
scene.add(synapseGroup);

const activeSynapses = []; // {head, tail, t, dur, from, to, payload, color, paused}

function spawnSynapse({ from, to, color = 'cyan', payload = {}, dur = 0.5, gold = false }) {
  // resolve neuron meshes
  const fromN = ALIASES[from], toN = ALIASES[to];
  if (!fromN || !toN) return null;
  const fromMesh = nodeMeshes.find(m => m.userData.neuron.id === fromN.id);
  const toMesh   = nodeMeshes.find(m => m.userData.neuron.id === toN.id);
  if (!fromMesh || !toMesh) return null;

  const a = fromMesh.position.clone();
  const b = toMesh.position.clone();
  // arc midpoint pushed outward
  const mid = a.clone().add(b).multiplyScalar(0.5);
  mid.normalize().multiplyScalar(SPHERE_R + 0.42);
  const curve = new THREE.QuadraticBezierCurve3(a, mid, b);

  // tail line (gradient in shader is overkill — use thin static line at low opacity)
  const tailPts = curve.getPoints(50);
  const tailGeo = new THREE.BufferGeometry().setFromPoints(tailPts);
  const tailColor = gold ? 0xE8C56A : (color === 'cyan' ? 0x3DDCFF : 0x7C8AFF);
  const tailMat = new THREE.LineBasicMaterial({ color: tailColor, transparent: true, opacity: 0.12, blending: THREE.AdditiveBlending });
  const tail = new THREE.Line(tailGeo, tailMat);
  synapseGroup.add(tail);

  // head
  const headGeo = new THREE.SphereGeometry(0.04, 14, 14);
  const headMat = new THREE.MeshBasicMaterial({ color: tailColor, transparent: true, opacity: 1, blending: THREE.AdditiveBlending });
  const head = new THREE.Mesh(headGeo, headMat);
  head.position.copy(a);
  synapseGroup.add(head);

  // halo around head
  const headHaloGeo = new THREE.SphereGeometry(0.13, 18, 18);
  const headHaloMat = new THREE.MeshBasicMaterial({ color: tailColor, transparent: true, opacity: 0.35, blending: THREE.AdditiveBlending, depthWrite: false });
  const headHalo = new THREE.Mesh(headHaloGeo, headHaloMat);
  head.add(headHalo);

  // flare endpoints
  flareNode(fromMesh, 1);
  setTimeout(() => flareNode(toMesh, 1), dur * 700);

  // fire cluster glow
  fireCluster(fromN.cluster, 0.6);
  setTimeout(() => fireCluster(toN.cluster, 1.0), dur * 700);

  const syn = {
    head, tail, headHalo,
    curve,
    t: 0, dur,
    paused: false,
    from, to, payload, gold,
  };
  activeSynapses.push(syn);
  return syn;
}

export function flareNode(mesh, mag = 1) {
  mesh.userData.flare = Math.max(mesh.userData.flare, mag);
}

export function fireCluster(clusterId, mag = 1) {
  clusterGlowGroup.children.forEach(g => {
    if (g.userData.cluster.id === clusterId) {
      g.userData.fire = Math.max(g.userData.fire, mag);
    }
  });
}

// camera control
let camTheta = 0, camPhi = 0.05;
let targetTheta = 0, targetPhi = 0.05;
let camDist = 4.5, targetDist = 4.5;
let dragging = false, lastX = 0, lastY = 0;
let autoOrbit = true;
let autoFocusEnabled = true;
let focusTarget = null; // cluster id

canvas.addEventListener('pointerdown', e => { dragging = true; lastX = e.clientX; lastY = e.clientY; autoOrbit = false; });
window.addEventListener('pointerup',  () => { dragging = false; });
window.addEventListener('pointermove', e => {
  if (!dragging) return;
  const dx = (e.clientX - lastX) / 200;
  const dy = (e.clientY - lastY) / 200;
  lastX = e.clientX; lastY = e.clientY;
  targetTheta -= dx;
  targetPhi  = Math.max(-1.0, Math.min(1.0, targetPhi - dy));
});
canvas.addEventListener('wheel', e => {
  e.preventDefault();
  targetDist = Math.max(2.6, Math.min(7, targetDist + e.deltaY * 0.003));
}, { passive: false });
canvas.addEventListener('dblclick', () => {
  targetTheta = 0; targetPhi = 0.05; targetDist = 4.5; autoOrbit = true; focusTarget = null;
});

// click neuron / synapse
const raycaster = new THREE.Raycaster();
const mouse = new THREE.Vector2();
canvas.addEventListener('click', e => {
  if (Math.abs(e.movementX) > 4 || Math.abs(e.movementY) > 4) return;
  const rect = canvas.getBoundingClientRect();
  mouse.x = ((e.clientX - rect.left) / rect.width) * 2 - 1;
  mouse.y = -((e.clientY - rect.top)  / rect.height) * 2 + 1;
  raycaster.setFromCamera(mouse, camera);
  // synapse heads first
  const synHits = raycaster.intersectObjects(activeSynapses.map(s => s.head));
  if (synHits.length) {
    const head = synHits[0].object;
    const syn = activeSynapses.find(s => s.head === head);
    if (syn) { syn.paused = !syn.paused; window.dispatchEvent(new CustomEvent('ino-synapse-click', { detail: { syn, screenX: e.clientX, screenY: e.clientY } })); }
    return;
  }
  const hits = raycaster.intersectObjects(nodeMeshes);
  if (hits.length) {
    const m = hits[0].object;
    window.dispatchEvent(new CustomEvent('ino-neuron-click', { detail: { neuron: m.userData.neuron, mesh: m } }));
    flareNode(m, 1.4);
  }
});

// Cluster labels (HTML overlay positioned via project to screen)
const labelEls = {};
CLUSTERS.forEach(c => {
  const el = document.createElement('div');
  el.className = 'cluster-label';
  el.innerHTML = `<span>${c.label}</span><span class="alias">${c.count} neurons</span>`;
  labelLayer.appendChild(el);
  labelEls[c.id] = el;
});

function project(v3) {
  const v = v3.clone().project(camera);
  return {
    x: (v.x * 0.5 + 0.5) * renderer.domElement.clientWidth,
    y: (-v.y * 0.5 + 0.5) * renderer.domElement.clientHeight,
    z: v.z,
  };
}

// API to set focus from outside (auto-focus on most active cluster)
export function focusCluster(id) {
  if (!autoFocusEnabled) return;
  focusTarget = id;
}
export function setAutoFocus(on) { autoFocusEnabled = on; if (!on) focusTarget = null; }

// Public synapse spawner
export function fireSynapse(opts) {
  return spawnSynapse(opts);
}

// Animate
const clock = new THREE.Clock();
let lastT = 0;

function animate() {
  const dt = clock.getDelta();
  const tNow = clock.elapsedTime;

  // auto orbit
  if (autoOrbit) targetTheta += 0.05 * dt;
  if (autoFocusEnabled && focusTarget) {
    const c = CLUSTERS.find(c => c.id === focusTarget);
    if (c) {
      const v = new THREE.Vector3(...c.pos);
      const targT = Math.atan2(v.x, v.z);
      const targP = Math.asin(v.y / Math.max(0.001, v.length()));
      targetTheta += (targT - targetTheta) * 0.02;
      targetPhi   += (targP - targetPhi)   * 0.04;
    }
  }
  camTheta += (targetTheta - camTheta) * 0.08;
  camPhi   += (targetPhi   - camPhi)   * 0.08;
  camDist  += (targetDist  - camDist)  * 0.08;

  camera.position.x = camDist * Math.cos(camPhi) * Math.sin(camTheta);
  camera.position.y = camDist * Math.sin(camPhi);
  camera.position.z = camDist * Math.cos(camPhi) * Math.cos(camTheta);
  camera.lookAt(0, 0, 0);

  // node pulses
  for (const m of nodeMeshes) {
    const ud = m.userData;
    // idle 0.2 Hz pulse
    const pulse = 1 + 0.06 * Math.sin(tNow * 1.2 + ud.neuron.id.charCodeAt(1));
    let scale = pulse;
    let opacity = ud.baseOpacity;
    if (ud.flare > 0) {
      ud.flare = Math.max(0, ud.flare - dt * 1.6);
      scale += ud.flare * 1.6;
      opacity = Math.min(1, opacity + ud.flare * 0.3);
      m.material.color.setHex(0x6CF5FF);
    } else {
      m.material.color.setHex(ud.cluster.color);
    }
    m.scale.setScalar(scale);
    m.material.opacity = opacity;

    // halo
    const halo = ud.halo;
    halo.material.opacity = 0.12 + ud.flare * 0.55;
    halo.scale.setScalar(1 + ud.flare * 0.6);
  }

  // cluster glow
  clusterGlowGroup.children.forEach(g => {
    const ud = g.userData;
    if (ud.fire > 0) ud.fire = Math.max(0, ud.fire - dt * 0.9);
    g.material.opacity = ud.baseOpacity + ud.fire * 0.32;
    g.scale.setScalar(1 + ud.fire * 0.5);
  });

  // filaments
  filamentGroup.children.forEach(l => {
    l.material.opacity = l.userData.baseOpacity + 0.04 * Math.sin(tNow * 0.6);
  });

  // synapses
  for (let i = activeSynapses.length - 1; i >= 0; i--) {
    const s = activeSynapses[i];
    if (!s.paused) s.t += dt / s.dur;
    const u = Math.min(1, s.t);
    const p = s.curve.getPoint(u);
    s.head.position.copy(p);
    // tail fade with progress
    s.tail.material.opacity = 0.10 + (1 - u) * 0.30;
    s.head.material.opacity = 0.6 + 0.4 * (1 - u);
    s.headHalo.material.opacity = 0.25 + 0.45 * (1 - Math.abs(u - 0.5) * 2);

    if (s.t >= 1.05 && !s.paused) {
      synapseGroup.remove(s.head); synapseGroup.remove(s.tail);
      s.head.geometry.dispose(); s.head.material.dispose();
      s.tail.geometry.dispose(); s.tail.material.dispose();
      activeSynapses.splice(i, 1);
    }
  }

  // update HTML labels
  CLUSTERS.forEach(c => {
    const el = labelEls[c.id];
    const center = new THREE.Vector3(...c.pos).normalize().multiplyScalar(SPHERE_R + 0.45);
    const proj = project(center);
    if (proj.z > 1) {
      el.style.opacity = 0;
    } else {
      el.style.opacity = Math.max(0.2, 1 - proj.z);
      el.style.left = `${proj.x}px`;
      el.style.top  = `${proj.y}px`;
    }
  });

  renderer.render(scene, camera);
  requestAnimationFrame(animate);
}
animate();

// Expose for synapse tooltip positioning
export function projectVec3(v) { return project(v); }
export { activeSynapses };

// auto-focus toggle
const autoFocusToggle = document.getElementById('tok-autofocus');
if (autoFocusToggle) {
  autoFocusToggle.addEventListener('change', e => setAutoFocus(e.target.checked));
}

// cluster ghost lingering — soft fading "you can see what just happened"
export function lingerGhost(clusterIds = []) {
  clusterIds.forEach(id => fireCluster(id, 0.5));
}
