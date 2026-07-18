---
inclusion: fileMatch
fileMatchPattern: 'src/TripRadar.WebUI/**'
---

# Shared UI Components — Usage Guide

When building or modifying frontend components in `src/TripRadar.WebUI`, always prefer shared components from `shared/ui` over native HTML elements or custom inline implementations.

## Available Components

All imports from `shared/ui`:

```ts
import { Button, DatePicker, Dropdown, Input, SearchInput, Textarea, Switch, Modal, Pagination, SectionEmpty, SectionError, SectionSkeleton, LoadingSpinner } from 'shared/ui';
```

## Component Usage Rules

### Button
Use `Button` for all clickable actions. Never use raw `<button>` with custom styling for primary/secondary/destructive actions.

```tsx
<Button variant="primary" size="md" isLoading={isPending}>Save</Button>
<Button variant="secondary" size="sm" onClick={onCancel}>Cancel</Button>
<Button variant="destructive">Delete</Button>
<Button variant="ghost">Dismiss</Button>
```

Variants: `primary`, `secondary`, `destructive`, `ghost`. Sizes: `sm`, `md`, `lg`.

### Input
Use `Input` for all text/email/password/number fields. Never use raw `<input>` with custom border/focus styling.

```tsx
<Input type="text" value={value} onChange={e => setValue(e.target.value)} placeholder="..." />
<Input type="email" error={hasError} />
```

### Textarea
Use `Textarea` for multi-line text. Never use raw `<textarea>` with custom styling.

```tsx
<Textarea value={value} onChange={e => setValue(e.target.value)} rows={3} maxLength={2000} />
```

### DatePicker
Use `DatePicker` for all date selection. Never use `<Input type="date">` or native `<input type="date">`.

```tsx
<DatePicker
  value={dateValue}           // yyyy-MM-dd string
  onChange={v => setDate(v)}  // receives yyyy-MM-dd string
  min={todayString}
  placeholder={t('Select date')}
  aria-label={t('Start Date')}
/>
```

The DatePicker renders a portal-based calendar popup with month navigation, today shortcut, min/max constraints, and full dark mode support.

### Dropdown
Use `Dropdown` for all select/option pickers. Never use native `<select>` elements.

```tsx
<Dropdown
  value={selected}
  options={[{ value: 'a', label: 'Option A' }, { value: 'b', label: 'Option B' }]}
  onChange={v => setSelected(v)}
  searchable          // optional: adds search input
  aria-label="..."
/>
```

Supports generic types (`Dropdown<number>`), search, keyboard navigation, portal-based popup.

### Switch
Use `Switch` for boolean toggles. Never build custom toggle components.

```tsx
<Switch checked={isActive} onChange={onToggle} disabled={isToggling} aria-label="Toggle feature" />
```

### SearchInput
Use `SearchInput` for all autocomplete/suggestion text inputs backed by an API search. Never build custom inline autocomplete dropdowns.

```tsx
<SearchInput
  value={inputValue}
  onChange={onInputChange}
  onSelect={onSuggestionSelect}
  suggestions={suggestions}
  isFetching={isLoading}
  placeholder={t('Search locations...')}
  label={<span className="text-sm font-medium ...">{t('Location')}</span>}
  searchingLabel={t('Searching...')}
  noResultsLabel={t('No locations found')}
  aria-label={t('Location')}
/>
```

Suggestions use the `SearchSuggestion` interface: `{ key, label, secondary?, badge? }`. Supports Escape to close, blur/focus management, and min query length threshold.

### SectionEmpty
Use for empty states. Never build custom centered "no data" blocks.

```tsx
<SectionEmpty
  message={t('No items yet')}
  icon={<Clock3 className="h-6 w-6 text-content-muted dark:text-content-muted-dark" />}
  action={<Button variant="primary" size="sm" onClick={onCreate}>{t('Create first')}</Button>}
/>
```

### SectionError
Use for error states with retry. Never build custom error blocks.

```tsx
<SectionError message={t('Unable to load data')} onRetry={() => query.refetch()} />
```

### SectionSkeleton
Use as a wrapper for skeleton loading states.

```tsx
<SectionSkeleton>
  <div className="animate-pulse space-y-3">...</div>
</SectionSkeleton>
```

### Modal
Use for dialogs and confirmations.

### Pagination
Use for paginated lists when the shared Pagination component fits. For compact pagination (e.g., inside toolbars), use chevron buttons with dot indicators.

## Anti-Patterns to Avoid

| Instead of... | Use... |
|---------------|--------|
| `<input type="date">` | `DatePicker` |
| `<Input type="date">` | `DatePicker` |
| `<select>` with `<option>` | `Dropdown` |
| Custom inline empty state div | `SectionEmpty` |
| Custom inline error div with retry | `SectionError` |
| Raw `<textarea>` with custom classes | `Textarea` |
| Raw `<button>` with full styling | `Button` with appropriate variant |
| Custom toggle/switch | `Switch` |
| Custom inline autocomplete dropdown | `SearchInput` |

## Page-Level Patterns

### Loading States
Use dedicated skeleton components (e.g., `TripVaultCardSkeleton`, `RequestCardSkeleton`) that match the shape of the content they replace. Render 3 skeletons as a default.

### Error States
Use `SectionError` at the section level with early return, not inside list components.

### Empty States
Use `SectionEmpty` with an icon and optional CTA button. Place at the section level.

### Page Wrapper
Profile pages use a thin shell pattern:
```tsx
<ProfileLayout>
  <div className="px-4 sm:px-6 lg:px-8 pb-4 sm:pb-6 lg:pb-8">
    <FeatureSection />
  </div>
</ProfileLayout>
```

### Toolbar Pattern
Page size selector + refresh button, aligned right:
```tsx
<div className="flex items-center gap-3">
  <div className="w-[120px]">
    <Dropdown value={pageSize} options={options} onChange={onPageSizeChange} className="!py-1 !px-2 !text-[11px]" />
  </div>
  <button type="button" onClick={onRefresh} className="p-1.5 rounded-md ..." aria-label={t('Refresh')}>
    <RefreshCw className="h-3.5 w-3.5" />
  </button>
</div>
```

## i18n Rules

- All user-visible text must use `t('key')` from `useFrontendLanguage()`
- Every key must exist in both `enTranslation` and `ruTranslation` in `shared/i18n/frontendTranslations.ts`
- Use short Russian translations for space-constrained UI (e.g., `page` → `стр.`)

## Maintaining This Document

When a new shared component is added to `shared/ui`, update this steering file:
1. Add the component to the "Available Components" import list
2. Add a usage section with example code under "Component Usage Rules"
3. Add relevant anti-patterns to the "Anti-Patterns to Avoid" table
