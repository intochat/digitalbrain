import assert from 'node:assert/strict'
import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

const testDirectory = dirname(fileURLToPath(import.meta.url))
const repositoryRoot = resolve(testDirectory, '..', '..')
const docsRoot = join(repositoryRoot, 'docs')

const read = (...segments) => readFileSync(join(repositoryRoot, ...segments), 'utf8')

const contentPages = [
  'index.md', 'concepts.md', 'architecture.md', 'quickstart.md', 'contributing.md',
  'packages.md', 'specification.md',
]

test('documentation project exposes the standard VitePress commands', () => {
  const packageJson = JSON.parse(read('docs', 'package.json'))

  assert.equal(packageJson.private, true)
  assert.equal(packageJson.type, 'module')
  assert.match(packageJson.scripts.dev, /vitepress dev/)
  assert.equal(packageJson.scripts.build, 'vitepress build')
  assert.match(packageJson.scripts.preview, /vitepress preview/)
  assert.equal(packageJson.scripts.test, 'node --test tests/*.test.mjs')
  assert.equal(packageJson.devDependencies.vitepress, '^1.6.4')
})

test('the specification is regenerated before the site is tested or built', () => {
  const packageJson = JSON.parse(read('docs', 'package.json'))

  assert.equal(packageJson.scripts.pretest, 'node tools/render-specification.mjs')
  assert.equal(packageJson.scripts.prebuild, 'node tools/render-specification.mjs')
  assert.equal(existsSync(join(docsRoot, 'tools', 'render-specification.mjs')), true)
})

test('every documented page exists and nothing else claims to be documentation', () => {
  for (const page of contentPages) {
    assert.equal(existsSync(join(docsRoot, page)), true, `${page} must exist`)
  }

  const retiredSections = ['guide', 'build', 'getting-started', 'contributing', 'reference', 'packages', 'status.md']
  for (const section of retiredSections) {
    assert.equal(existsSync(join(docsRoot, section)), false, `${section}/ must stay deleted`)
  }
  assert.equal(existsSync(join(docsRoot, '.vitepress', 'theme')), false)
})

test('navigation and sidebar reach every page', () => {
  const config = read('docs', '.vitepress', 'config.mts')

  assert.match(config, /title: 'DigitalBrain'/)
  assert.match(config, /provider: 'local'/)
  assert.match(config, /github\.com\/intochat\/digitalbrain/)
  assert.doesNotMatch(config, /InteractiveAgents/)

  for (const link of ['/quickstart', '/concepts', '/architecture', '/specification', '/packages', '/contributing']) {
    assert.ok(config.includes(`'${link}'`), `config must link ${link}`)
  }
})

test('the homepage tells the neurons, synapses, and simulations story', () => {
  const homepage = read('docs', 'index.md')

  assert.match(homepage, /layout: home/)
  assert.match(homepage, /Neurons/)
  assert.match(homepage, /Synapses/)
  assert.match(homepage, /Simulations/)
  assert.match(homepage, /Orleans/)
  assert.match(homepage, /Aspire/)
})

test('concepts define the three primitives and the whole vocabulary', () => {
  const concepts = read('docs', 'concepts.md')

  assert.match(concepts, /IHandle<TSynapse>/)
  assert.match(concepts, /IEmit<TSynapse>/)
  assert.match(concepts, /journaled grain/)
  assert.match(concepts, /correlation and causation lineage/)
  assert.match(concepts, /dev-only/)

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

test('the architecture page is module-organized and states each status once', () => {
  const architecture = read('docs', 'architecture.md')

  for (const heading of [
    'The vision', 'The kernel', 'The module model', 'The modules',
    'Behaviors and scripting', 'Registry and discovery', 'Hosting and durability',
  ]) {
    assert.ok(architecture.includes(heading), `architecture must have a ${heading} section`)
  }

  for (const module of ['AI', 'Tasks', 'Google', 'Salesforce', 'Time', 'Flutter', 'Memory']) {
    assert.ok(
      new RegExp(`### .*\\b${module}\\b`).test(architecture),
      `architecture must have a ${module} module section`)
  }

  const built = architecture.match(/^Status: Built$/gm) ?? []
  const designed = architecture.match(/^Status: Designed$/gm) ?? []
  assert.equal(built.length, 4, 'AI, Tasks, Google, and Salesforce are built')
  assert.equal(designed.length, 2, 'Time and Flutter are designed')

  assert.match(architecture, /human-approved proposal/)
  assert.match(architecture, /Runtime behavior installation is designed and not yet built/)
  assert.doesNotMatch(architecture, /REFINED-ARCHITECTURE|APPROVED-ARCHITECTURE/)
})

test('the quickstart matches the sample that CI actually runs', () => {
  const quickstart = read('docs', 'quickstart.md')
  const program = read('samples', 'DigitalBrain.Quickstart', 'Program.cs')
  const neurons = read('samples', 'DigitalBrain.Quickstart', 'Neurons.cs')

  for (const call of ['UseLocalhostClustering()', 'AddDigitalBrain()', 'AddDevelopmentJournalStorage()']) {
    assert.ok(program.includes(call), `the sample must still call ${call}`)
    assert.ok(quickstart.includes(call), `the quickstart must document ${call}`)
  }

  assert.ok(neurons.includes('interface IGreeter'), 'the sample must expose a typed neuron contract')
  assert.ok(neurons.includes('record SayHello'), 'the sample must declare its incoming synapse')
  assert.ok(program.includes('SendAsync<IGreeter>'), 'the sample must use the owner-bound client entry point')
  assert.ok(quickstart.includes('interface IGreeter'), 'the quickstart must show the real contract')
  assert.ok(quickstart.includes('SendAsync<IGreeter>'), 'the quickstart must show the real client call')
  assert.match(quickstart, /samples\/DigitalBrain\.Quickstart/)
})

test('the specification publishes every Tier-1 feature file verbatim', () => {
  const simulations = join(repositoryRoot, 'tests', 'DigitalBrain.Simulations')
  const features = readdirSync(simulations).filter(entry => entry.endsWith('.feature'))
  const specification = read('docs', 'specification.md')

  assert.ok(features.length > 0, 'there must be Tier-1 feature files to publish')

  for (const feature of features) {
    const source = readFileSync(join(simulations, feature), 'utf8').trimEnd()
    assert.ok(specification.includes(source), `${feature} must be published verbatim`)
  }
})

test('every shipped package is in the table, and the boundary is stated', () => {
  const packages = read('docs', 'packages.md')
  const packableSource = read('tests', 'DigitalBrain.Tests', 'PackableProjects.cs')

  const packable = [...packableSource.matchAll(/"(DigitalBrain[^"]*)"/g)].map(match => match[1])
  assert.ok(packable.length >= 18, `expected the packable list, found ${packable.length}`)

  for (const name of packable) {
    assert.ok(packages.includes(`\`${name}\``), `the table must list ${name}`)
  }

  assert.ok(packages.includes('`DigitalBrain`'), 'the table must list the metapackage')
  assert.match(packages, /Provider SDKs live only in `DigitalBrain\.Modules\.AI`/)
  assert.match(packages, /does \*\*not\*\* reference `DigitalBrain\.Kernel`/)
  assert.match(packages, /refuses to start/i)
  assert.match(packages, /namespace and type name are the model identity/i)
  assert.match(packages, /openai-api-key/)
})

test('the contributing guide states the gate and the non-negotiable rules', () => {
  const contributing = read('docs', 'contributing.md')

  assert.match(contributing, /dotnet test \.\\DigitalBrain\.slnx -c Release/)
  assert.match(contributing, /--filter/)
  assert.match(contributing, /Comments are forbidden/i)
  assert.match(contributing, /Tier 0/)
  assert.match(contributing, /Tier 1/)
  assert.match(contributing, /Tier 2/)
})

test('the open debts are disclosed rather than buried', () => {
  const architecture = read('docs', 'architecture.md')

  assert.match(architecture, /trusted cluster peer/)
  assert.match(architecture, /Journal history is bounded/)
  assert.match(architecture, /Effectively-once processing is also windowed/)
  assert.match(architecture, /FIFO per target/)
  assert.match(architecture, /Delivery ordering/)
  assert.match(architecture, /Broadcast addressing/)
  assert.match(architecture, /handler \*\*types\*\*/)
  assert.match(architecture, /timeline stream/)
  assert.match(architecture, /AsClient/)
  assert.match(architecture, /DevUI/)

  assert.match(read('docs', 'packages.md'), /not\*\* an authentication boundary/)
})

test('the ratified rules survive as a checklist', () => {
  const architecture = read('docs', 'architecture.md')

  const sectionStart = /^## 9\. Ratified rules$/m.exec(architecture)
  assert.ok(sectionStart, 'the "## 9. Ratified rules" heading must exist')

  const afterSectionStart = architecture.slice(sectionStart.index + sectionStart[0].length)
  const sectionEnd = /^## 10\..*$/m.exec(afterSectionStart)
  assert.ok(sectionEnd, 'the "## 10." heading that closes section 9 must exist')

  const ratifiedRulesSection = afterSectionStart.slice(0, sectionEnd.index)
  assert.ok(
    ratifiedRulesSection.trim().length > 0,
    'the ratified rules section between "## 9." and "## 10." must not be empty')

  for (let rule = 1; rule <= 47; rule += 1) {
    assert.match(
      ratifiedRulesSection,
      new RegExp(`^${rule}\\. \\S`, 'm'),
      `ratified rule ${rule} is missing from section 9`)
  }
  assert.doesNotMatch(ratifiedRulesSection, /^48\. /m, 'the ratified list ends at 47')

  for (const rejected of ['Ical.Net', 'Durable Extension', 'model tier', 'raw invoke']) {
    assert.ok(architecture.includes(rejected), `the rejected list must name ${rejected}`)
  }
})

test('no page resurrects rejected v1 vocabulary', () => {
  const v1Identifiers = /INeuronKind|INeuronContract|NeuronGrain|NeuronProxy|\bBrain\.slnx|neuron_invoke/
  const rejectedArchitecture = new RegExp([
    'Model' + 'Tier',
    'Model' + 'Providers',
    'IModel' + 'CompletionService',
    'Ask' + 'ModelAsync',
    'With' + 'Model\\(',
    'AddAI' + 'Module',
    'AddDigitalBrain' + 'Models',
    'ChatModel' + 'Neuron',
    'Scripted' + 'Model',
    '\\bBrain' + 'Client\\b',
  ].join('|'))

  for (const page of contentPages) {
    assert.doesNotMatch(read('docs', page), v1Identifiers, `${page} must not mention v1 identifiers`)
    assert.doesNotMatch(read('docs', page), rejectedArchitecture, `${page} must not teach the rejected architecture`)
  }
  assert.doesNotMatch(read('docs', '.vitepress', 'config.mts'), v1Identifiers)
  assert.doesNotMatch(read('README.md'), v1Identifiers)
})

test('the repository README points at the v2 gate and story', () => {
  const readme = read('README.md')

  assert.match(readme, /neurons,\s+synapses, and simulations/i)
  assert.match(readme, /DigitalBrain\.slnx/)
  assert.doesNotMatch(readme, /kernel\/|edge\/|workspace\//)
})

test('generated VitePress, npm and specification output stays out of git', () => {
  const gitignore = read('.gitignore')

  assert.match(gitignore, /^node_modules\/$/m)
  assert.match(gitignore, /^docs\/\.vitepress\/cache\/$/m)
  assert.match(gitignore, /^docs\/\.vitepress\/dist\/$/m)
  assert.match(gitignore, /^docs\/specification\.md$/m)
})
