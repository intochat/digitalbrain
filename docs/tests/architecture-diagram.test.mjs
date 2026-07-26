import assert from 'node:assert/strict'
import test from 'node:test'
import { KERNEL, MODULES, ACTORS, BEHAVIORS } from '../.vitepress/theme/architecture-data.js'

test('every diagram module is typed and links into the architecture page', () => {
  assert.ok(MODULES.length > 0)

  for (const module of MODULES) {
    assert.ok(
      ['built', 'designed', 'scope'].includes(module.status),
      `${module.id} has status '${module.status}'`)
    assert.match(module.section, /^#[\w-]+$/, `${module.id} needs an in-page anchor`)
    assert.ok(Array.isArray(module.neurons))
    assert.ok(Array.isArray(module.synapses))

    for (const resource of module.aspire) {
      assert.ok(resource.res, `${module.id} has an unnamed Aspire resource`)
      assert.ok(Array.isArray(resource.params))
    }
  }
})

test('no diagram behaviour composes vocabulary that nothing ships', () => {
  const shipped = new Set(KERNEL.owns)
  for (const module of MODULES) {
    module.neurons.forEach(neuron => shipped.add(neuron))
    module.synapses.forEach(synapse => shipped.add(synapse))
  }

  assert.ok(BEHAVIORS.length > 0)

  for (const behaviour of BEHAVIORS) {
    assert.ok(behaviour.uses.length > 0, `${behaviour.id} composes nothing`)

    for (const token of behaviour.uses) {
      assert.ok(shipped.has(token), `${behaviour.id} composes ${token}, which nothing ships`)
    }
  }
})

test('the diagram never shows a behaviour as built while the rail is unbuilt', () => {
  for (const behaviour of BEHAVIORS) {
    assert.equal(behaviour.status, 'designed', `${behaviour.id} must stay designed`)
    assert.match(behaviour.script, /Behavior/, `${behaviour.id} must show the Behavior base`)
  }
})

test('the kernel and its actors are typed', () => {
  assert.deepEqual(ACTORS.map(actor => actor.id), ['people', 'agents'])
  assert.ok(ACTORS.every(actor => ['built', 'designed'].includes(actor.status)))
  assert.ok(KERNEL.owns.includes('Neuron'))
  assert.ok(KERNEL.owns.includes('Synapse'))
  assert.match(KERNEL.section, /^#[\w-]+$/)
})
