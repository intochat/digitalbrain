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

  assert.match(homepage, /Everything addressable is a neuron/)
  assert.match(homepage, /An operating system built from neurons and synapses/)
  assert.match(homepage, /Explore the architecture/)
  assert.match(config, /title: 'DigitalBrain'/)
  assert.match(config, /provider: 'local'/)
  assert.match(config, /Guide/)
  assert.match(config, /Contributing/)
  assert.match(config, /Reference/)
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

test('mobile hero keeps the DigitalBrain brand on one line', () => {
  const css = read('website', '.vitepress', 'theme', 'custom.css')

  assert.match(css, /font-size: clamp\(52px, 18vw, 72px\);/)
  assert.match(css, /white-space: nowrap;/)
})
