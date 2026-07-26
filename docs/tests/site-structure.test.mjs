import assert from 'node:assert/strict'
import { existsSync } from 'node:fs'
import { join } from 'node:path'
import test from 'node:test'
import { contentPages, docsRoot, read } from './support.mjs'

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
  // the interactive architecture diagram lives in the site's one custom theme
  assert.equal(existsSync(join(docsRoot, '.vitepress', 'theme', 'index.js')), true)
  assert.equal(existsSync(join(docsRoot, '.vitepress', 'theme', 'ArchitectureMap.vue')), true)
  const themeIndex = read('docs', '.vitepress', 'theme', 'index.js')
  assert.match(themeIndex, /extends:\s*DefaultTheme/)
  assert.match(themeIndex, /app\.component\('ArchitectureMap'/)
  assert.match(read('docs', 'architecture.md'), /<ArchitectureMap\s*\/>/)
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

test('the site is configured for the digitalbrain.tech GitHub Pages apex', () => {
  const config = read('docs', '.vitepress', 'config.mts')
  const cname = read('docs', 'public', 'CNAME').trim()
  const pagesWorkflow = read('.github', 'workflows', 'docs-pages.yml')

  assert.match(config, /base:\s*['"]\/['"]/)
  assert.match(config, /hostname:\s*['"]https:\/\/digitalbrain\.tech['"]/)
  assert.equal(cname, 'digitalbrain.tech')
  assert.match(pagesWorkflow, /npm test/)
  assert.match(pagesWorkflow, /npm run build/)
  assert.match(pagesWorkflow, /actions\/upload-pages-artifact@v5/)
  assert.match(pagesWorkflow, /actions\/deploy-pages@v5/)
  assert.match(pagesWorkflow, /path:\s*docs\/\.vitepress\/dist/)
  assert.match(pagesWorkflow, /cancel-in-progress:\s*false/)
  assert.match(pagesWorkflow, /branches:\s*\[master\]/)
})

test('CI keeps framework on every master event and docs only on pull requests', () => {
  const ci = read('.github', 'workflows', 'ci.yml')
  const pages = read('.github', 'workflows', 'docs-pages.yml')
  const dependabot = read('.github', 'dependabot.yml')

  assert.match(ci, /^ {2}framework:/m)
  assert.match(ci, /dotnet test DigitalBrain\.slnx -c Release/)
  assert.match(ci, /if:\s*github\.event_name\s*==\s*'pull_request'/)
  assert.match(ci, /npm test/)
  assert.match(ci, /npm run build/)
  assert.match(ci, /cache-dependency-path:\s*docs\/package-lock\.json/)
  assert.doesNotMatch(ci, /deploy-pages/)
  assert.doesNotMatch(ci, /nuget\.org|dotnet nuget push|dotnet pack/i)

  assert.match(pages, /npm ci --no-audit --no-fund/)
  assert.match(pages, /npm test/)
  assert.match(pages, /npm run build/)
  assert.doesNotMatch(pages, /dotnet test/)

  assert.match(dependabot, /package-ecosystem:\s*github-actions/)
  assert.match(dependabot, /package-ecosystem:\s*npm/)
  assert.match(dependabot, /directory:\s*\/docs/)
  assert.match(dependabot, /package-ecosystem:\s*nuget/)
})
