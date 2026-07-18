---
inclusion: fileMatch
fileMatchPattern: '**/content/changelog.ts'
---

# Changelog Entry Guide

## Adding a New Entry

Add a new object to the `changelogEntries` array in `src/TripRadar.WebUI/src/pages/marketing/content/changelog.ts`. Entries are sorted by `publishedAt` automatically (newest first).

## Entry Structure

```ts
{
  id: 'kebab-case-unique-slug',        // URL-safe, unique across all entries
  publishedAt: '2026-MM-DD',           // ISO date string
  title: { en: 'English title', ru: 'Русский заголовок' },
  summary: { en: 'One-two sentence description', ru: 'Краткое описание' },
  blocks: [/* content blocks */],
}
```

## Content Blocks

### Paragraph
```ts
{ type: 'paragraph', text: { en: '...', ru: '...' } }
```

### Bullet List
```ts
{
  type: 'list',
  items: [
    { en: 'First item', ru: 'Первый пункт' },
    { en: 'Second item', ru: 'Второй пункт' },
  ],
}
```

### Image
```ts
{
  type: 'image',
  src: '/filename.png',                // Place file in public/
  alt: { en: 'Description', ru: 'Описание' },
  layout: 'cover',                     // 'cover' (full width) or 'inline' (constrained height)
}
```

### Call to Action (use sparingly)
```ts
{
  type: 'cta',
  label: { en: 'Button text', ru: 'Текст кнопки' },
  href: '/path',
  external: false,                     // true for external links
}
```

## Rules

- Every text field must have both `en` and `ru` values
- `id` must be unique and kebab-case
- `publishedAt` must be a valid ISO date (YYYY-MM-DD)
- Images go in `src/TripRadar.WebUI/public/` and are referenced with leading `/`
- Keep summaries to 1-2 sentences — they appear directly under the title
- Prefer `paragraph` and `list` blocks — avoid CTA blocks unless there's a specific action
- Don't duplicate links that are already in the site footer (feedback, help center)
