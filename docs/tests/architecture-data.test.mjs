import assert from 'node:assert/strict'
import test from 'node:test'
import { KERNEL, MODULES, ACTORS, BEHAVIORS } from '../.vitepress/theme/architecture-data.js'

const MODULE_IDS = ['ai', 'tasks', 'google', 'salesforce', 'time', 'flutter', 'memory']

test('every module is a known module, correctly typed, with an in-page section link', () => {
  assert.equal(MODULES.length, MODULE_IDS.length)
  for (const m of MODULES) {
    assert.ok(MODULE_IDS.includes(m.id), `unknown module id: ${m.id}`)
    assert.ok(['built', 'designed', 'scope'].includes(m.status), `${m.id} has a bad status`)
    assert.match(m.section, /^#[\w-]+$/, `${m.id} section must be an in-page anchor`)
    assert.ok(Array.isArray(m.neurons) && Array.isArray(m.synapses) && Array.isArray(m.aspire))
    for (const a of m.aspire) {
      assert.ok(a.res && Array.isArray(a.params), `${m.id} aspire entry malformed`)
    }
  }
})

test('every behaviour composes only vocabulary that a module or the kernel actually ships', () => {
  const vocab = new Set(KERNEL.owns)
  for (const m of MODULES) {
    m.neurons.forEach(n => vocab.add(n))
    m.synapses.forEach(s => vocab.add(s))
  }
  assert.ok(BEHAVIORS.length > 0)
  for (const b of BEHAVIORS) {
    assert.ok(b.uses.length > 0, `behaviour ${b.id} composes nothing`)
    assert.ok(b.script.includes('Behavior'), `behaviour ${b.id} script must show the Behavior base`)
    for (const token of b.uses) {
      assert.ok(vocab.has(token), `behaviour ${b.id} composes ${token}, which nothing ships`)
    }
  }
})

test('the actors and the kernel are present and typed', () => {
  assert.deepEqual(ACTORS.map(a => a.id), ['people', 'agents'])
  for (const a of ACTORS) assert.ok(['built', 'designed'].includes(a.status))
  assert.ok(KERNEL.owns.includes('Neuron') && KERNEL.owns.includes('Synapse'))
  assert.match(KERNEL.section, /^#[\w-]+$/)
})
