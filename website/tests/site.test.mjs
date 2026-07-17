import assert from 'node:assert/strict'
import { existsSync, readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

const testDirectory = dirname(fileURLToPath(import.meta.url))
const repositoryRoot = resolve(testDirectory, '..', '..')
const websiteRoot = join(repositoryRoot, 'website')

const read = (...segments) => readFileSync(join(repositoryRoot, ...segments), 'utf8')

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

test('initial open-source documentation set exists', () => {
  const requiredPages = [
    'index.md',
    'getting-started/index.md',
    'getting-started/first-call.md',
    'build/first-module.md',
    'guide/index.md',
    'guide/architecture.md',
    'guide/neurons.md',
    'guide/synapses.md',
    'guide/modules.md',
    'guide/programming-model.md',
    'guide/webhooks.md',
    'contributing/index.md',
    'reference/status.md',
    'reference/decisions.md'
  ]

  for (const page of requiredPages) {
    assert.equal(existsSync(join(websiteRoot, page)), true, `${page} must exist`)
  }
})

test('homepage and navigation present the DigitalBrain vision', () => {
  const homepage = read('website', '.vitepress', 'theme', 'components', 'HomePage.vue')
  const config = read('website', '.vitepress', 'config.mts')

  assert.match(homepage, /What runs today/)
  assert.match(homepage, /INeuronKind/)
  assert.match(homepage, /INeuronContract/)
  assert.match(homepage, /Implemented/)
  assert.match(homepage, /Target/)
  assert.match(homepage, /Decision/)
  assert.match(homepage, /First MCP call/)
  assert.doesNotMatch(homepage, /Kernel online/)
  assert.doesNotMatch(homepage, />Memory</)
  assert.doesNotMatch(homepage, />Stripe</)
  assert.doesNotMatch(homepage, />Community</)
  assert.match(config, /title: 'DigitalBrain'/)
  assert.match(config, /provider: 'local'/)
  assert.match(config, /Getting Started/)
  assert.match(config, /Concepts/)
  assert.match(config, /Build/)
  assert.match(config, /\{ text: 'Status', link: '\/reference\/status' \}/)
  assert.match(config, /Contributing/)
})

test('Aspire starts the documentation as an external Vite resource', () => {
  const appHost = read('hosts', 'DigitalBrain.AppHost', 'AppHost.cs')
  const appHostProject = read('hosts', 'DigitalBrain.AppHost', 'DigitalBrain.AppHost.csproj')
  const centralPackages = read('Directory.Packages.props')

  assert.match(appHost, /AddViteApp\("brain-docs", "\.\.\/\.\.\/website"\)/)
  assert.match(appHost, /\.WithNpm\(installCommand: "ci", installArgs: \["--no-audit", "--no-fund"\]\)/)
  assert.match(appHost, /\.WithEnvironment\("NODE_ENV", "development"\)/)
  assert.match(appHost, /\.WithExternalHttpEndpoints\(\)/)
  assert.match(appHostProject, /PackageReference Include="Aspire\.Hosting\.JavaScript"/)
  assert.match(centralPackages, /PackageVersion Include="Aspire\.Hosting\.JavaScript" Version="13\.4\.6"/)
})

test('generated VitePress and npm output stays out of git', () => {
  const gitignore = read('.gitignore')

  assert.match(gitignore, /^node_modules\/$/m)
  assert.match(gitignore, /^website\/\.vitepress\/cache\/$/m)
  assert.match(gitignore, /^website\/\.vitepress\/dist\/$/m)
})

test('mobile hero uses fluid typography without forcing an overflow-prone wordmark', () => {
  const css = read('website', '.vitepress', 'theme', 'custom.css')
  const mobileRule = css.match(/@media \(max-width: 640px\) \{[\s\S]*?\.hero-content h1 \{([\s\S]*?)\n  \}/)

  assert.ok(mobileRule, 'mobile hero heading rule must exist')
  assert.match(mobileRule[1], /font-size: clamp\(/)
  assert.doesNotMatch(mobileRule[1], /white-space:\s*nowrap/)
  assert.match(css, /overflow-wrap:\s*anywhere/)
})

test('concept documentation separates implemented behavior from target architecture', () => {
  const neurons = read('website', 'guide', 'neurons.md')
  const architecture = read('website', 'guide', 'architecture.md')
  const modules = read('website', 'guide', 'modules.md')
  const webhooks = read('website', 'guide', 'webhooks.md')
  const status = read('website', 'reference', 'status.md')

  assert.match(neurons, /INeuronContract/)
  assert.match(neurons, /NeuronProxy/)
  assert.match(neurons, /owner\|space\|kind\/instance/)
  assert.match(architecture, /NeuronGrain/)
  assert.match(architecture, /INeuronKind/)
  assert.match(architecture, /VolatileJournalStorageProvider/)
  assert.match(modules, /explicit host composition/i)
  assert.match(webhooks, /Target contract/)
  assert.match(webhooks, /IWebHookNeuron : INeuron/)
  assert.match(status, /hard-coded development caller/i)
  assert.match(status, /provider idempotency key/)
  assert.match(status, /llama3\.1:8b/)
  assert.doesNotMatch(status, /Flutter workspace/)
})

test('contributor journey starts with Aspire and reaches a real module test', () => {
  const firstCall = read('website', 'getting-started', 'first-call.md')
  const firstModule = read('website', 'build', 'first-module.md')

  assert.match(firstCall, /aspire run/)
  assert.match(firstCall, /neuron_describe/)
  assert.match(firstCall, /neuron_invoke/)
  assert.match(firstCall, /local-owner\|main\|chat\/main/)
  assert.match(firstCall, /base URL/)
  assert.doesNotMatch(firstCall, /`\/mcp`/)
  assert.match(firstModule, /INeuronContract/)
  assert.match(firstModule, /INeuronKind/)
  assert.match(firstModule, /Brain\.ConformanceTests/)
  assert.match(firstModule, /Brain\.Kernel\.Host\/Program\.cs/)
})

test('repository entry point directs readers to current evidence instead of superseded design', () => {
  const readme = read('README.md')

  assert.doesNotMatch(readme, /EVERYTHING-IS-A-NEURON\.md/)
  assert.doesNotMatch(readme, /INO operation/)
  assert.match(readme, /INeuronKind/)
  assert.match(readme, /website\/reference\/status\.md/)
  assert.equal(existsSync(join(repositoryRoot, 'EVERYTHING-IS-A-NEURON.md')), false)
})

test('homepage section links remain in normal flow outside legacy primitive cards', () => {
  const css = read('website', '.vitepress', 'theme', 'custom.css')
  const textLinkRule = css.match(/\.text-link \{([\s\S]*?)\n\}/)
  const systemCopyRule = css.match(/\.system-copy \{([\s\S]*?)\n\}/)
  const programmingModel = read('website', 'guide', 'programming-model.md')

  assert.ok(textLinkRule, 'text link rule must exist')
  assert.doesNotMatch(textLinkRule[1], /position:\s*absolute/)
  assert.ok(systemCopyRule, 'system copy rule must exist')
  assert.match(systemCopyRule[1], /min-height:\s*0/)
  assert.match(programmingModel, /var reply = await web\.FetchAsync\(request\)/)
  assert.doesNotMatch(programmingModel, /var receipt = await web\.FetchAsync\(request\)/)
})
