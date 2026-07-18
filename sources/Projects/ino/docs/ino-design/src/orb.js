// Persona orb states + sparkle on celebrate
const core = document.getElementById('orb-core');
const status = document.getElementById('orb-status');

const COPY = {
  idle:        'ino · listening for you',
  listening:   'ino · hearing you',
  thinking:    'ino · composing',
  speaking:    'ino · responding',
  confused:    'ino · could you say that again?',
  celebrating: 'ino · done',
};

export function setOrb(state) {
  core.dataset.state = state;
  status.textContent = COPY[state] ?? COPY.idle;
  if (state === 'celebrating') {
    sparkle();
    setTimeout(() => setOrb('idle'), 800);
  }
}

function sparkle() {
  const wrap = document.querySelector('.orb-wrap').getBoundingClientRect();
  for (let i = 1; i <= 3; i++) {
    const el = document.getElementById('sparkle-' + i);
    el.style.left = '50%';
    el.style.top = '50%';
    el.style.opacity = 1;
    el.animate(
      [
        { transform: 'translate(-50%, -50%) scale(0.4)', opacity: 1 },
        { transform: `translate(${(-50 + (i-2)*60)}%, ${-150 - i*20}%) scale(${0.6 + i*0.1})`, opacity: 0 },
      ],
      { duration: 900, easing: 'cubic-bezier(0.22,1,0.36,1)', fill: 'forwards' }
    );
  }
}

// Tilt orb toward most-active cluster (visual cue)
export function tiltOrb(dx = 0, dy = 0) {
  core.style.transform = `translate(${dx}px, ${dy}px)`;
}
