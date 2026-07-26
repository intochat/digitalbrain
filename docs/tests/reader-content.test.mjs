import assert from 'node:assert/strict'
import test from 'node:test'
import { read } from './support.mjs'

test('the homepage tells the neurons, synapses, and executable tests story', () => {
  const homepage = read('docs', 'index.md')

  assert.match(homepage, /layout: home/)
  assert.match(homepage, /Neurons/)
  assert.match(homepage, /Synapses/)
  assert.match(homepage, /TestBrain/)
  assert.match(homepage, /Orleans/)
  assert.match(homepage, /Aspire/)
})

test('concepts define the three primitives and the whole vocabulary', () => {
  const concepts = read('docs', 'concepts.md')

  assert.match(concepts, /IHandle<TSynapse>/)
  assert.match(concepts, /IEmit<TSynapse>/)
  assert.match(concepts, /journaled grain/)
  assert.match(concepts, /correlation and causation lineage/)
  assert.match(concepts, /method-scoped development testing primitive/)

  const glossaryTerms = [
    'Neuron', 'Synapse', 'Capability request', 'Module', 'Behavior', 'Registry',
    'LLM', 'Agent', 'Orchestration', 'Group Chat', 'Participant', 'Executor', 'Capability',
    'Task', 'Goal', 'Attempt', 'Worker', 'Workflow', 'Blocker', 'Result', 'Successor Task',
    'Countdown', 'Reminder', 'Interval schedule', 'Calendar schedule', 'Occurrence',
  ]
  for (const term of glossaryTerms) {
    assert.ok(concepts.includes(`**${term}**`), `concepts must define ${term}`)
  }

  const avoidLines = concepts.match(/^_Avoid_: /gm) ?? []
  assert.ok(avoidLines.length >= 26, `every glossary term needs an _Avoid_ line, found ${avoidLines.length}`)
})

test('the quickstart matches the sample that CI actually runs', () => {
  const quickstart = read('docs', 'quickstart.md')
  const contract = read('samples', 'DigitalBrain.Quickstart.Contracts', 'IGreeter.cs')
  const synapses = read('samples', 'DigitalBrain.Quickstart.Contracts', 'GreetingSynapses.cs')
  const module = read('samples', 'DigitalBrain.Quickstart', 'QuickstartModule.cs')
  const host = read('hosts', 'DigitalBrain.Quickstart.Host', 'Program.cs')
  const appHost = read('hosts', 'DigitalBrain.Quickstart.AppHost', 'AppHost.cs')
  const fixture = read('tests', 'DigitalBrain.Quickstart.Tests', 'QuickstartFixture.cs')

  assert.match(quickstart, /^---\r?\ntitle: Quickstart\r?\n---/)
  assert.ok(contract.includes('interface IGreeter'), 'the contracts package must expose IGreeter')
  assert.ok(synapses.includes('record SayHello'), 'the contracts package must expose SayHello')
  assert.ok(synapses.includes('record Greeted'), 'the contracts package must expose Greeted')
  assert.ok(module.includes('partial class QuickstartModule'), 'the runtime must expose its compiled module')
  assert.ok(host.includes('AddDigitalBrain()'), 'the compiled host must install the kernel')
  assert.match(appHost, /"quickstart"/, 'AppHost brain identity remains quickstart')
  assert.match(
    appHost,
    /AddDigitalBrain\(\s*(?:Brain|"quickstart")\s*\)/,
    'AppHost must own infrastructure via AddDigitalBrain')
  assert.ok(appHost.includes('AddModule<QuickstartModule>()'), 'AppHost must select the compiled module')
  assert.ok(fixture.includes('AddModule<QuickstartModule>()'), 'tests must select the same compiled module')
  assert.ok(quickstart.includes('interface IGreeter'), 'the quickstart must show the real contract')
  assert.ok(quickstart.includes('SendAsync<IGreeter>'), 'the quickstart must show the real client call')
  assert.ok(quickstart.includes('IDigitalBrain brain'), 'the endpoint must receive the client through DI')
  assert.ok(quickstart.includes('.WithReference(brain.AsClient())'), 'AppHost must project a client reference')
  assert.doesNotMatch(quickstart, /Host\.CreateApplicationBuilder|GetRequiredService|host\.StartAsync/)
  assert.match(quickstart, /not a product Behavior install/)
})

test('the specification describes the retained test tiers', () => {
  const specification = read('docs', 'specification.md')

  assert.match(specification, /# Specification/)
  assert.match(specification, /DigitalBrain\.Quickstart\.Tests/)
  assert.match(specification, /DigitalBrain\.Time\.Tests/)
  assert.match(specification, /DigitalBrain\.HostTests/)
  assert.doesNotMatch(specification, /DigitalBrain\.Simulations/)
  assert.doesNotMatch(specification, /ModuleDriver/)
})

test('the contributing guide states the gate and the non-negotiable rules', () => {
  const contributing = read('docs', 'contributing.md')

  assert.match(contributing, /dotnet test \.\\DigitalBrain\.slnx -c Release/)
  assert.match(contributing, /--filter/)
  assert.match(contributing, /Comments are forbidden/i)
  assert.match(contributing, /Tier 0/)
  assert.match(contributing, /Tier 1/)
  assert.match(contributing, /Tier 2/)
  assert.match(contributing, /digitalbrain\.tech/)
  assert.match(contributing, /docs-pages\.yml/)
  assert.match(contributing, /GitHub Actions/)
  assert.match(contributing, /CI and CD/)
  assert.match(contributing, /pull requests only/i)
  assert.match(contributing, /does \*\*not\*\* wait on the framework/)
  assert.match(contributing, /dependabot\.yml/)
})
