import { readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const testDirectory = dirname(fileURLToPath(import.meta.url))
const repositoryRoot = resolve(testDirectory, '..', '..')

export const docsRoot = join(repositoryRoot, 'docs')

export const read = (...segments) => readFileSync(join(repositoryRoot, ...segments), 'utf8')

export const linkedPages = () => {
  const config = read('docs', '.vitepress', 'config.mts')
  const links = [...config.matchAll(/link:\s*'(\/[^']*)'/g)].map(match => match[1])
  return [...new Set(links)].map(link => ({
    link,
    page: link === '/' ? 'index.md' : `${link.replace(/^\//, '')}.md`,
  }))
}
