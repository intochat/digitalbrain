---
inclusion: fileMatch
fileMatchPattern: 'src/TripRadar.WebUI/**'
---

# i18n Guidelines

## Setup

- Library: `react-i18next` with custom wrapper
- Languages: `en` (English, default), `ru` (Russian)
- Translation file: `shared/i18n/frontendTranslations.ts`
- Hook: `useFrontendLanguage()` from `app/providers` — returns `{ t, language }`

## Golden Rule

Every user-visible string in a component must use `t('key')`. No hardcoded text in JSX — ever. When any text is added or changed in a component, the corresponding translation key and translations must be added to `frontendTranslations.ts` in the same change.

## Translation File Structure

```ts
// shared/i18n/frontendTranslations.ts

export const enTranslation = {
  'key': 'English text',
  // ...
} as const;

export type FrontendTranslationKey = keyof typeof enTranslation;

export const ruTranslation: Record<string, string> = {
  ...enTranslation,          // fallback to English for missing keys
  'key': 'Русский текст',
  // ...
};
```

`ruTranslation` spreads `enTranslation` first, then overrides with Russian values. Any key not overridden falls back to English.

## Adding New Text — Checklist

When you add any user-visible text to a component:

1. Add the key + English value to `enTranslation` (before `} as const;`)
2. Add the key + Russian translation to `ruTranslation` (before the closing `};`)
3. Use `t('key')` in the component
4. If the text has parameters, use interpolation: `t('Hello {name}', { name: userName })`

Never skip step 2. Both languages must always have every key.

## Key Naming Conventions

| Pattern | When to use | Example |
|---------|-------------|---------|
| Plain English text | Short UI labels, buttons, messages | `'Save Changes'`, `'No trips yet'` |
| Dotted namespace | Scoped to a specific page/section | `'profile.layout.trips'`, `'profile.preferences.title'` |
| Sentence with `{param}` | Dynamic content | `'Delete "{tripName}"? This action cannot be undone.'` |

Rules:
- Use plain English text as keys for most UI strings — it's self-documenting
- Use dotted namespaces only for page-specific labels that might collide (e.g., `profile.layout.billing`)
- Keep keys identical to the English text when possible
- Interpolation params use `{paramName}` syntax (curly braces, camelCase)

## Interpolation

```tsx
// In component:
t('Showing {from} - {to} of {total}', { from: 1, to: 10, total: 25 })

// In enTranslation:
'Showing {from} - {to} of {total}': 'Showing {from} - {to} of {total}',

// In ruTranslation:
'Showing {from} - {to} of {total}': 'Показано {from} - {to} из {total}',
```

Interpolation params must be preserved in all translations — same names, same braces.

## Russian Translation Tips

- Keep translations concise — Russian text is often longer than English
- For space-constrained UI (dropdowns, badges), use abbreviations: `page` → `стр.`
- Avoid formal/informal mixing — use informal "вы" consistently
- Test that translated text fits the UI (buttons, badges, dropdown items)

## What Must Be Translated

| Element | Translate? | Example |
|---------|-----------|---------|
| Button labels | ✅ | `t('Save Changes')` |
| Page headings | ✅ | `t('Trip History')` |
| Descriptions / subtitles | ✅ | `t('Remove stale entries...')` |
| Placeholder text | ✅ | `placeholder={t('e.g. Spring in Lisbon')}` |
| Toast titles and messages | ✅ | `showSuccess(t('Trip created'), t('New trip vault has been created.'))` |
| `aria-label` attributes | ✅ | `aria-label={t('Delete')}` |
| Error messages | ✅ | `t('Unable to load trips')` |
| Empty state messages | ✅ | `t('No history items yet')` |
| `title` tooltips | ✅ | `title={t('Vault is inactive outside selected dates.')}` |
| Console logs | ❌ | `console.error('Failed to load')` — English only |
| Code comments | ❌ | `// Fetch trips` — English only |
| CSS class names | ❌ | Not translatable |

## Using `t()` Outside Components

For utility functions that need translations (e.g., `formatDateTime` returning "Not set"):

```ts
import { frontendI18n } from 'app/i18n';

export const formatDateTime = (value?: string | null): string => {
  if (!value) return frontendI18n.t('Not set');
  // ...
};
```

Use `frontendI18n.t()` directly — not the hook. This is for non-React contexts only.

## Validation

There is a test in `shared/i18n/frontendTranslations.test.ts` that validates translations. When adding keys, ensure:
- The key exists in `enTranslation`
- The Russian override exists in `ruTranslation`
- Interpolation params match between languages

## Maintaining This Document

When new i18n patterns or conventions are established, update this file accordingly.
