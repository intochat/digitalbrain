import assert from 'node:assert/strict'
import test from 'node:test'
import { read } from './support.mjs'

test('the quickstart teaches the contract the sample actually ships', () => {
  const quickstart = read('docs', 'quickstart.md')
  const contract = read('samples', 'DigitalBrain.Quickstart.Contracts', 'IGreeter.cs')
  const synapses = read('samples', 'DigitalBrain.Quickstart.Contracts', 'GreetingSynapses.cs')

  assert.match(contract, /interface IGreeter/)
  assert.match(synapses, /record SayHello/)
  assert.match(synapses, /record Greeted/)

  assert.match(quickstart, /interface IGreeter/)
  assert.match(quickstart, /SendAsync<IGreeter>/)
  assert.match(quickstart, /IDigitalBrain brain/)
})

test('the quickstart selects the same module the host and its tests select', () => {
  const quickstart = read('docs', 'quickstart.md')
  const module = read('samples', 'DigitalBrain.Quickstart', 'QuickstartModule.cs')
  const appHost = read('hosts', 'DigitalBrain.Quickstart.AppHost', 'AppHost.cs')
  const fixture = read('tests', 'DigitalBrain.Quickstart.Tests', 'QuickstartFixture.cs')

  assert.match(module, /partial class QuickstartModule/)

  for (const source of [quickstart, appHost, fixture]) {
    assert.match(source, /AddModule<QuickstartModule>\(\)/)
  }
})
