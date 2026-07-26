import { readFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const testDirectory = dirname(fileURLToPath(import.meta.url))
const repositoryRoot = resolve(testDirectory, '..', '..')
export const docsRoot = join(repositoryRoot, 'docs')

export const read = (...segments) => readFileSync(join(repositoryRoot, ...segments), 'utf8')

export const contentPages = [
  'index.md', 'concepts.md', 'architecture.md', 'quickstart.md', 'contributing.md',
  'packages.md', 'specification.md',
]
