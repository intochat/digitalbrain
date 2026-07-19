import assert from 'node:assert/strict'
import { existsSync, readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

const testDirectory = dirname(fileURLToPath(import.meta.url))
const repositoryRoot = resolve(testDirectory, '..', '..')
const websiteRoot = join(repositoryRoot, 'website')

const read = (...segments) => readFileSync(join(repositoryRoot, ...segments), 'utf8')

const contentPages = ['index.md', 'concepts.md', 'status.md']

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

test('the v2 skeleton pages exist and nothing else claims to be documentation', () => {
  for (const page of contentPages) {
    assert.equal(existsSync(join(websiteRoot, page)), true, `${page} must exist`)
  }

  const retiredSections = ['guide', 'build', 'getting-started', 'contributing', 'reference']
  for (const section of retiredSections) {
    assert.equal(existsSync(join(websiteRoot, section)), false, `${section}/ must stay deleted`)
  }
  assert.equal(existsSync(join(websiteRoot, '.vitepress', 'theme')), false)
})

test('navigation covers exactly the skeleton pages', () => {
  const config = read('website', '.vitepress', 'config.mts')

  assert.match(config, /title: 'DigitalBrain'/)
  assert.match(config, /provider: 'local'/)
  assert.match(config, /\{ text: 'Concepts', link: '\/concepts' \}/)
  assert.match(config, /\{ text: 'Status', link: '\/status' \}/)
  assert.match(config, /github\.com\/digitalbraintech\/brain/)
  assert.doesNotMatch(config, /InteractiveAgents/)
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

test('status stays truthful about the rebuild', () => {
  const status = read('website', 'status.md')

  assert.match(status, /ground-up rebuild/)
  assert.match(status, /No packages are published/)
  assert.match(status, /dotnet test DigitalBrain\.slnx -c Release/)
  assert.match(status, /npm run build/)
})

test('no page resurrects rejected v1 vocabulary', () => {
  const v1Identifiers = /INeuronKind|INeuronContract|NeuronGrain|NeuronProxy|\bBrain\.slnx|neuron_invoke|VolatileJournalStorageProvider/

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
  assert.doesNotMatch(readme, /kernel\/|modules\/|edge\/|hosts\/|workspace\//)
})

test('generated VitePress and npm output stays out of git', () => {
  const gitignore = read('.gitignore')

  assert.match(gitignore, /^node_modules\/$/m)
  assert.match(gitignore, /^website\/\.vitepress\/cache\/$/m)
  assert.match(gitignore, /^website\/\.vitepress\/dist\/$/m)
})
