import assert from 'node:assert/strict'
import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

const testDirectory = dirname(fileURLToPath(import.meta.url))
const repositoryRoot = resolve(testDirectory, '..', '..')
const websiteRoot = join(repositoryRoot, 'website')

const read = (...segments) => readFileSync(join(repositoryRoot, ...segments), 'utf8')

const packagePages = [
  'metapackage', 'abstractions', 'kernel', 'client', 'testing', 'aspire', 'aspire-hosting', 'devtools',
]

const contentPages = [
  'index.md', 'concepts.md', 'architecture.md', 'quickstart.md', 'contributing.md', 'status.md',
  'specification.md', 'packages/index.md', ...packagePages.map(page => `packages/${page}.md`),
]

test('documentation project exposes the standard VitePress commands', () => {
  const packageJson = JSON.parse(read('website', 'package.json'))

  assert.equal(packageJson.private, true)
  assert.equal(packageJson.type, 'module')
  assert.match(packageJson.scripts.dev, /vitepress dev/)
  assert.equal(packageJson.scripts.build, 'vitepress build')
  assert.match(packageJson.scripts.preview, /vitepress preview/)
  assert.equal(packageJson.scripts.test, 'node --test tests/*.test.mjs')
  assert.equal(packageJson.devDependencies.vitepress, '^1.6.4')
})

test('the specification is regenerated before the site is tested or built', () => {
  const packageJson = JSON.parse(read('website', 'package.json'))

  assert.equal(packageJson.scripts.pretest, 'node tools/render-specification.mjs')
  assert.equal(packageJson.scripts.prebuild, 'node tools/render-specification.mjs')
  assert.equal(existsSync(join(websiteRoot, 'tools', 'render-specification.mjs')), true)
})

test('every documented page exists and nothing else claims to be documentation', () => {
  for (const page of contentPages) {
    assert.equal(existsSync(join(websiteRoot, page)), true, `${page} must exist`)
  }

  const retiredSections = ['guide', 'build', 'getting-started', 'contributing', 'reference']
  for (const section of retiredSections) {
    assert.equal(existsSync(join(websiteRoot, section)), false, `${section}/ must stay deleted`)
  }
  assert.equal(existsSync(join(websiteRoot, '.vitepress', 'theme')), false)
})

test('navigation and sidebar reach every page', () => {
  const config = read('website', '.vitepress', 'config.mts')

  assert.match(config, /title: 'DigitalBrain'/)
  assert.match(config, /provider: 'local'/)
  assert.match(config, /github\.com\/digitalbraintech\/brain/)
  assert.doesNotMatch(config, /InteractiveAgents/)

  for (const link of ['/quickstart', '/concepts', '/architecture', '/specification', '/packages/', '/contributing', '/status']) {
    assert.ok(config.includes(`'${link}'`), `config must link ${link}`)
  }

  for (const page of packagePages) {
    assert.ok(config.includes(`/packages/${page}`), `config must link /packages/${page}`)
  }
})

test('the homepage tells the neurons, synapses, and simulations story', () => {
  const homepage = read('website', 'index.md')

  assert.match(homepage, /layout: home/)
  assert.match(homepage, /Neurons/)
  assert.match(homepage, /Synapses/)
  assert.match(homepage, /Simulations/)
  assert.match(homepage, /Orleans/)
  assert.match(homepage, /Aspire/)
})

test('concepts define the three primitives and the scope fence', () => {
  const concepts = read('website', 'concepts.md')

  assert.match(concepts, /IHandle<TSynapse>/)
  assert.match(concepts, /IEmit<TSynapse>/)
  assert.match(concepts, /journaled grain/)
  assert.match(concepts, /correlation and causation lineage/)
  assert.match(concepts, /dev-only package/)
  assert.match(concepts, /out of scope/)
})

test('the architecture page separates what is built from what is designed', () => {
  const architecture = read('website', 'architecture.md')

  assert.match(architecture, /designed and not yet built/)
  assert.match(architecture, /Modules own vocabulary|Vocabulary — synapse records/)
  assert.match(architecture, /Behaviors own logic|Logic over existing vocabulary/)
  assert.match(architecture, /human-approved proposal/)

  const sections = architecture.split(/^## /m).slice(1)
  const designSections = sections.filter(section => /Status: /.test(section))
  assert.ok(designSections.length >= 4, 'each design section must carry an explicit status line')

  const status = read('website', 'status.md')
  assert.match(status, /Where this is going/)
  assert.match(status, /load-bearing and unmeasured/)
})

test('the quickstart matches the sample that CI actually runs', () => {
  const quickstart = read('website', 'quickstart.md')
  const program = read('samples', 'DigitalBrain.Quickstart', 'Program.cs')
  const neurons = read('samples', 'DigitalBrain.Quickstart', 'Neurons.cs')

  for (const call of ['UseLocalhostClustering()', 'AddDigitalBrain()', 'AddDevelopmentJournalStorage()']) {
    assert.ok(program.includes(call), `the sample must still call ${call}`)
    assert.ok(quickstart.includes(call), `the quickstart must document ${call}`)
  }

  assert.ok(neurons.includes('IHandle<Hello>'), 'the sample neuron must still handle Hello')
  assert.ok(quickstart.includes('IHandle<Hello>'), 'the quickstart must show the real neuron')
  assert.match(quickstart, /samples\/DigitalBrain\.Quickstart/)
})

test('the specification publishes every Tier-1 feature file verbatim', () => {
  const simulations = join(repositoryRoot, 'tests', 'DigitalBrain.Simulations')
  const features = readdirSync(simulations).filter(entry => entry.endsWith('.feature'))
  const specification = read('website', 'specification.md')

  assert.ok(features.length > 0, 'there must be Tier-1 feature files to publish')

  for (const feature of features) {
    const source = readFileSync(join(simulations, feature), 'utf8').trimEnd()
    assert.ok(specification.includes(source), `${feature} must be published verbatim`)
  }
})

test('every shipped package has a page, and the boundary is stated', () => {
  const index = read('website', 'packages', 'index.md')

  for (const page of packagePages) {
    assert.ok(index.includes(`/packages/${page}`), `the package index must link ${page}`)
  }

  assert.match(index, /only in `DigitalBrain\.Kernel`/)
  assert.match(read('website', 'packages', 'metapackage.md'), /does \*\*not\*\* reference \[`DigitalBrain\.Kernel`\]/)
  assert.match(read('website', 'packages', 'kernel.md'), /refuses to start/i)
})

test('the contributing guide states the gate and the non-negotiable rules', () => {
  const contributing = read('website', 'contributing.md')

  assert.match(contributing, /dotnet test \.\\DigitalBrain\.slnx -c Release/)
  assert.match(contributing, /--filter/)
  assert.match(contributing, /Comments are forbidden/i)
  assert.match(contributing, /Tier 0/)
  assert.match(contributing, /Tier 1/)
  assert.match(contributing, /Tier 2/)
})

test('status stays truthful about the rebuild', () => {
  const status = read('website', 'status.md')

  assert.match(status, /ground-up rebuild/)
  assert.match(status, /No packages are published/)
  assert.match(status, /dotnet test \.\\DigitalBrain\.slnx -c Release/)
  assert.match(status, /npm run build/)
})

test('the open debts are disclosed rather than buried', () => {
  const status = read('website', 'status.md')

  assert.match(status, /timeline stream/)
  assert.match(status, /AsClient/)
  assert.match(status, /DevUI/)
  assert.match(status, /trusted cluster peer/)
  assert.match(status, /history is lost/)
  assert.match(status, /Effectively-once processing is windowed/)
  assert.match(status, /Broadcast addressing/)
  assert.match(status, /handler \*\*types\*\*/)
  assert.match(status, /Delivery ordering/)
  assert.match(status, /FIFO \*\*per target\*\*/)

  const changelog = read('CHANGELOG.md')
  assert.match(changelog, /Known limitations/)
  assert.match(changelog, /timeline stream/)
  assert.match(changelog, /trusted cluster peer/)

  assert.match(read('website', 'packages', 'client.md'), /not\*\* an authentication boundary/)
})

test('no page resurrects rejected v1 vocabulary', () => {
  const v1Identifiers = /INeuronKind|INeuronContract|NeuronGrain|NeuronProxy|\bBrain\.slnx|neuron_invoke/

  for (const page of contentPages) {
    assert.doesNotMatch(read('website', page), v1Identifiers, `${page} must not mention v1 identifiers`)
  }
  assert.doesNotMatch(read('website', '.vitepress', 'config.mts'), v1Identifiers)
  assert.doesNotMatch(read('README.md'), v1Identifiers)
})

test('the repository README points at the v2 gate and story', () => {
  const readme = read('README.md')

  assert.match(readme, /neurons,\s+synapses, and simulations/i)
  assert.match(readme, /DigitalBrain\.slnx/)
  assert.doesNotMatch(readme, /kernel\/|modules\/|edge\/|workspace\//)
})

test('generated VitePress, npm and specification output stays out of git', () => {
  const gitignore = read('.gitignore')

  assert.match(gitignore, /^node_modules\/$/m)
  assert.match(gitignore, /^website\/\.vitepress\/cache\/$/m)
  assert.match(gitignore, /^website\/\.vitepress\/dist\/$/m)
  assert.match(gitignore, /^website\/specification\.md$/m)
})
