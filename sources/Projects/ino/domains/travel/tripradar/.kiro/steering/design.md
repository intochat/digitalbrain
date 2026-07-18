---
inclusion: auto
---

# TripRadar Design System

## 1. Visual Theme & Atmosphere

TripRadar is a travel planning tool with a minimal, content-first design. The UI is clean and functional — no decorative gradients, no illustrations, no visual noise. Cards are flat (no shadows), borders are subtle, and spacing is generous but not wasteful. The dark theme uses warm near-black tones (`#0f0f0f`) instead of pure black, creating a comfortable reading experience.

The overall feel is calm and professional. Think "well-organized travel notebook" — information-dense but never cluttered. Every element earns its place. The design inverts the convention of bold, heavy UI: headings use `font-medium` (500) instead of bold, cards have no elevation, and the color palette is almost entirely neutral with two sparse brand accents.

Key characteristics:
- Inter font family with `font-medium` as the workhorse weight — no bold headings
- Flat cards with `rounded-lg` and thin borders — no shadows, no elevation
- Warm dark mode with three surface tiers (`#0f0f0f`, `#1a1a1a`, `#2a2a2a`)
- Yellow (`#eab308`) and cyan (`#06b6d4`) as sparse brand accents — used only for premium features and active states
- Status communicated through color dots and badges, never through borders or backgrounds
- `transition-colors duration-150` on all interactive elements
- Shadow-as-border technique for dropdowns only — cards and inputs use real CSS borders
- Optimistic UI updates for toggles — no loading spinners on state changes

## 2. Color Palette & Roles

### Surface & Background

| Token | Light | Dark | Role |
|-------|-------|------|------|
| `surface` | `#ffffff` | `#0f0f0f` | Page background, card background |
| `surface-dark-secondary` | — | `#1a1a1a` | Secondary panels, sidebars |
| `surface-dark-tertiary` | — | `#242424` | Tertiary backgrounds |
| `surface-accent` | `#f1f5f9` | `#2a2a2a` | Hover states, skeleton placeholders |
| `surface-accent-dark-hover` | — | `#323232` | Hover on accent surfaces |

### Text & Content

| Token | Light | Dark | Role |
|-------|-------|------|------|
| `content` | `#0f172a` | `#f8f9fa` | Primary text, headings |
| `content-secondary` | `#475569` | `#e9ecef` | Secondary text, labels, descriptions |
| `content-muted` | `#64748b` | `#adb5bd` | Placeholders, helper text, icons |
| `content-disabled-dark` | — | `#6c757d` | Disabled text in dark mode |

### Borders

| Token | Light | Dark | Role |
|-------|-------|------|------|
| `outline` | `#cbd5e1` | `#404040` | Primary borders (cards, inputs) |
| `outline-secondary` | `#94a3b8` | `#2d2d2d` | Subtle dividers |
| `outline-accent-dark` | — | `#4a4a4a` | Interactive borders |

### Interactive & Buttons

| Token | Light | Dark | Role |
|-------|-------|------|------|
| `button` | `#0f172a` | `#f8f9fa` | Primary button background |
| `button-text` | `#ffffff` | `#0f172a` | Primary button text |
| `button-hover` | `#1f2937` | `#e9ecef` | Primary button hover |
| `interactive-active` | `#ec4899` | `#06b6d4` | Active/selected state accent |

### Brand Accents (use sparingly)

| Token | Value | Usage |
|-------|-------|-------|
| `primary-500` | `#eab308` | Yellow accent — highlights, premium features |
| `secondary-500` | `#06b6d4` | Cyan accent — active states in dark mode |

### Status Colors (Tailwind defaults)

| Status | Color | Dark variant | Usage |
|--------|-------|-------------|-------|
| Success/Active | `emerald-500` | same | Active indicators, success messages |
| Error | `red-600` | `red-500` | Error states, destructive buttons |
| Warning | `amber-500` | same | Warnings |
| Paused/Inactive | `gray-400` | `gray-500` | Paused indicators |

## 3. Typography

### Font Stack
- Primary: `Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, Segoe UI, Roboto, Helvetica Neue, Arial, sans-serif`
- Heading line-height: `1.3`

### Type Hierarchy

| Role | Tailwind Classes | Usage |
|------|-----------------|-------|
| Page heading | `text-lg font-semibold text-content dark:text-content-dark` | Top-level page titles |
| Section heading | `text-base font-medium text-content dark:text-content-dark` | Section titles within pages |
| Card heading | `text-sm font-medium text-content dark:text-content-dark` | Card titles, list item titles |
| Body | `text-sm text-content dark:text-content-dark` | Standard reading text |
| Secondary body | `text-sm text-content-secondary dark:text-content-secondary-dark` | Descriptions, secondary info |
| Label | `text-xs text-content-secondary dark:text-content-secondary-dark` | Form labels, detail labels |
| Muted/helper | `text-xs text-content-muted dark:text-content-muted-dark` | Timestamps, helper text |
| Button text | `text-xs font-medium` (sm) / `text-sm font-medium` (md, lg) | Button labels |

### Typography Principles
- `font-medium` (500) is the default emphasis weight — used for headings, buttons, labels
- `font-semibold` (600) only for page-level headings
- Never use `font-bold` (700) for card headings or UI elements
- Never use `tracking-tight` — Inter at these sizes doesn't need it
- Always use `break-words` on user-generated content (titles, descriptions)

## 4. Component Patterns

### Cards

All cards follow the same pattern — no exceptions:

```
rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4 sm:p-5
```

Rules:
- Always `rounded-lg` (8px) — never `rounded-2xl` or `rounded-xl`
- No shadows (`shadow-sm`, `shadow-md`, etc.) — ever
- No colored left/top borders as status indicators
- Padding: `p-4 sm:p-5` (responsive, tighter on mobile)
- Internal layout: `flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between`

### Buttons

Four variants defined in `shared/ui/Button`:

| Variant | Background | Text | Hover | Border |
|---------|-----------|------|-------|--------|
| `primary` | `bg-button` / `dark:bg-button-dark` | `text-button-text` / `dark:text-button-text-dark` | `bg-button-hover` | none |
| `secondary` | `bg-surface` / `dark:bg-surface-dark` | `text-content` / `dark:text-content-dark` | `bg-surface-accent` | `border-outline` |
| `destructive` | `bg-red-600` / `dark:bg-red-500` | `text-white` | `bg-red-700` | none |
| `ghost` | transparent | `text-content-secondary` | `text-content` + `bg-surface-accent` | none |

Three sizes:
- `sm`: `px-3 py-1.5 text-xs`
- `md`: `px-4 py-2.5 text-sm`
- `lg`: `px-5 py-3 text-sm`

All buttons: `rounded-lg`, `font-medium`, `transition-colors duration-150`, `focus:ring-2 focus:ring-content/10`.

### Inputs & Selects

```
rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark
px-3 py-2.5 text-sm text-content dark:text-content-dark
placeholder-content-muted
focus:outline-none focus:border-content/40 dark:focus:border-content-dark/40
```

### Badges

```
rounded-full px-2.5 py-1 text-xs font-medium
```

Color varies by type (sky for flights, emerald for hotels, violet for events, amber for local places).

### Empty States

Use `SectionEmpty` component:
- Container: `border border-outline dark:border-outline-dark rounded-xl p-6`
- Layout: centered flex column with optional icon and action button
- Message: `text-sm text-content-secondary dark:text-content-secondary-dark`
- Icon: `h-6 w-6 text-content-muted dark:text-content-muted-dark`

### Switch / Toggle

- Track: `h-[22px] w-[40px] rounded-full transition-colors duration-200`
- Thumb: `h-4 w-4 rounded-full bg-white dark:bg-surface-dark transition-transform duration-200`
- On state: track `bg-content dark:bg-content-dark`, thumb `translate-x-[22px]`
- Off state: track `bg-outline dark:bg-outline-dark`, thumb `translate-x-[2px]`
- Disabled: `opacity-40 cursor-not-allowed`

### Icon Buttons (edit, delete, actions)

Neutral action:
```
rounded-lg p-2 text-content-secondary dark:text-content-secondary-dark
hover:bg-surface-accent dark:hover:bg-surface-accent-dark
```

Destructive action:
```
rounded-lg p-2 text-content-muted dark:text-content-muted-dark
hover:text-red-500 dark:hover:text-red-400
```

Icon size: `h-4 w-4` (standard) or `h-3.5 w-3.5` (compact).

### Status Indicators

- Color dot: `inline-flex h-2 w-2 rounded-full`
- Active: `bg-emerald-500`
- Paused: `bg-gray-400 dark:bg-gray-500`
- Always include `aria-label` for accessibility

### Skeleton / Loading States

- Container matches the component it replaces (same `rounded-lg`, padding, border)
- Placeholder bars: `bg-surface-accent dark:bg-surface-accent-dark rounded-md animate-pulse`
- Heights: `h-5` for text lines, `h-6` for badges, `h-9` for buttons
- Use `rounded-full` for badge placeholders, `rounded-md` for text placeholders

## 5. Layout Principles

### Spacing Scale

| Context | Value | Usage |
|---------|-------|-------|
| Between cards | `space-y-3` | Card lists |
| Between sections | `space-y-4` | Major content blocks |
| Page padding | `px-4 sm:px-6 lg:px-8` | Horizontal page margins |
| Page bottom padding | `pb-4 sm:pb-6 lg:pb-8` | Bottom spacing |
| Card internal | `gap-2` to `gap-4` | Between card elements |
| Button gap | `gap-2` | Between action buttons |
| Badge/indicator gap | `gap-2` | Between badges and indicators |

### Responsive Breakpoints

| Breakpoint | Width | Key Changes |
|------------|-------|-------------|
| `xs` | 475px | Custom small mobile breakpoint |
| `sm` | 640px | Two-column grids begin, padding increases |
| `md` | 768px | Tablet landscape adjustments |
| `lg` | 1024px | Desktop sidebar appears, flex-row layouts |
| `xl` | 1280px | Wide desktop, max content width |

### Grid Patterns

- Detail rows: `grid grid-cols-1 sm:grid-cols-2 gap-2`
- Mobile nav: `grid grid-cols-2 gap-2 sm:gap-3`
- Card content: `flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between`

### Collapsing Strategy

- Cards: desktop side-by-side layout → mobile stacked
- Navigation: desktop sidebar → mobile grid of tiles
- Action buttons: always horizontal, never stack
- Detail grids: 2 columns → 1 column on mobile
- Page padding: decreases at each breakpoint step down

### Touch Targets

- Icon buttons: minimum `p-2` (32px touch area)
- Navigation tiles: full-width on mobile with `touch-manipulation`
- Switch/toggle: `h-[22px] w-[40px]` — adequate for thumb taps
- Buttons: `py-1.5` (sm) / `py-2.5` (md) / `py-3` (lg) — all exceed 32px minimum

### Border Radius Scale

| Token | Value | Usage |
|-------|-------|-------|
| `rounded-md` | 6px | Skeleton text placeholders, small tags |
| `rounded-lg` | 8px | Cards, buttons, inputs, icon buttons — the default |
| `rounded-xl` | 12px | Empty state containers only |
| `rounded-full` | 9999px | Badges, toggles, status dots, skeleton badge placeholders |

Rules:
- `rounded-lg` is the system default — use it unless there's a specific reason not to
- Never use `rounded-2xl` (16px) — it's too soft for TripRadar's aesthetic
- `rounded-full` is reserved for pill-shaped elements (badges, dots, toggles)

## 6. Depth & Elevation

TripRadar uses a flat design — no shadow-based elevation system.

| Level | Treatment | Usage |
|-------|-----------|-------|
| Flat (default) | No shadow, border only | Cards, inputs, containers |
| Hover | `bg-surface-accent` background shift | Buttons, interactive elements |
| Focus | `ring-2 ring-content/10` | Keyboard focus on buttons |
| Input focus | `border-content/40` | Focused inputs and selects |
| Dropdown | `shadow-lg` (exception) | Autocomplete dropdowns, popovers |

Elevation is communicated through background color changes, not shadows. Hover states use `bg-surface-accent`, active states use `bg-surface-accent-dark-hover`.

## 7. Transitions & Animation

| Property | Duration | Easing | Usage |
|----------|----------|--------|-------|
| `transition-colors` | `150ms` | default ease | Buttons, links, icon buttons |
| `transition-colors` | `200ms` | default ease | Switch track color |
| `transition-transform` | `200ms` | default ease | Switch thumb position |
| `animate-pulse` | — | built-in | Skeleton loading states |
| `animate-spin` | — | built-in | Loading spinners (Loader2 icon) |

Rules:
- Use `transition-colors duration-150` on all interactive elements
- Never use `transition-all` — be explicit about what transitions
- No entrance/exit animations on cards or sections
- No parallax, no scroll animations

## 8. Accessibility

- All icon-only buttons MUST have `aria-label`
- Status indicators (dots) MUST have `aria-label` ("Active" / "Paused")
- Switch component uses `role="switch"` with `aria-checked`
- Focus visible: `focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-outline`
- Disabled state: `disabled:opacity-50 disabled:cursor-not-allowed` (buttons), `disabled:opacity-40` (switch)
- Touch targets: minimum `p-2` (32px) for icon buttons, `touch-manipulation` on mobile-interactive elements

## 9. Dark Mode

- Toggled via `class` strategy (`.dark` on root element)
- Every color class MUST have a `dark:` variant — no exceptions
- Dark surfaces use warm tones — never pure black (`#000000`)
- Dark text uses `#f8f9fa` — never pure white (`#ffffff`)

### Dark Surface Hierarchy

| Surface | Hex | Usage |
|---------|-----|-------|
| Base | `#0f0f0f` | Page background |
| Secondary | `#1a1a1a` | Panels, sidebars |
| Tertiary | `#242424` | Nested containers |
| Accent | `#2a2a2a` | Hover states, skeletons |
| Accent hover | `#323232` | Hover on accent surfaces |

## 10. Do's and Don'ts

### Do
- Use semantic tokens from `tailwind.config.mjs` — never hardcode hex values in components
- Always include `dark:` variants for every color class
- Use `rounded-lg` for cards, buttons, inputs
- Use `rounded-full` for badges, toggles, status dots
- Keep cards flat — no shadows
- Use `transition-colors duration-150` on interactive elements
- Use `font-medium` for emphasis — it's the workhorse weight
- Add `aria-label` to every icon-only button and status indicator
- Use optimistic updates for toggle/switch mutations

### Don't
- Don't use `shadow-sm`, `shadow-md`, or any box shadows on cards
- Don't use `rounded-2xl` or `rounded-xl` for cards (only `rounded-lg`)
- Don't use colored left/top borders as status indicators
- Don't use `font-bold` for card headings — use `font-medium`
- Don't use `tracking-tight` on headings
- Don't hardcode colors — always use semantic tokens
- Don't use pure black (`#000000`) or pure white (`#ffffff`) for text
- Don't use `transition-all` — be explicit about transition properties
- Don't add entrance/exit animations to content sections
- Don't use loading spinners in toggles — use optimistic updates instead
- Don't show duplicate CTAs (e.g., "create" button + empty state CTA simultaneously)

## 11. Agent Prompt Guide

### Quick Token Reference
- Page bg: `bg-surface dark:bg-surface-dark`
- Card: `rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4 sm:p-5`
- Primary text: `text-content dark:text-content-dark`
- Secondary text: `text-content-secondary dark:text-content-secondary-dark`
- Muted text: `text-content-muted dark:text-content-muted-dark`
- Border: `border-outline dark:border-outline-dark`
- Hover bg: `hover:bg-surface-accent dark:hover:bg-surface-accent-dark`
- Primary button: `Button variant="primary"`
- Secondary button: `Button variant="secondary"`

### Component Creation Checklist
1. Does every color class have a `dark:` variant?
2. Are cards using `rounded-lg` with no shadows?
3. Are buttons using the `Button` component from `shared/ui`?
4. Do icon buttons have `aria-label`?
5. Are interactive elements using `transition-colors`?
6. Does the layout collapse properly on mobile (`flex-col` → `flex-row` at `lg`)?
7. Are skeleton states matching the component's dimensions?

### Example Component Prompts

- "Create a card: `rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4 sm:p-5`. Title at `text-sm font-medium text-content dark:text-content-dark`. Body at `text-sm text-content-secondary dark:text-content-secondary-dark`. No shadows."

- "Create a detail row inside a card: `<span className='text-xs text-content-secondary dark:text-content-secondary-dark'>Label: </span><span className='text-sm text-content dark:text-content-dark'>Value</span>`. Grid: `grid grid-cols-1 sm:grid-cols-2 gap-2`."

- "Create an icon button: `rounded-lg p-2 text-content-secondary dark:text-content-secondary-dark hover:bg-surface-accent dark:hover:bg-surface-accent-dark transition-colors`. Icon at `h-4 w-4`. Must have `aria-label`."

- "Create a status badge: `rounded-full px-2.5 py-1 text-xs font-medium` with type-specific colors. Status dot: `inline-flex h-2 w-2 rounded-full bg-emerald-500` with `aria-label='Active'`."

- "Create a skeleton loader: match the target component's container exactly (`rounded-lg border border-outline dark:border-outline-dark bg-surface dark:bg-surface-dark p-4 sm:p-5`). Placeholder bars: `bg-surface-accent dark:bg-surface-accent-dark rounded-md animate-pulse`. Use `h-5` for text, `h-6 rounded-full` for badges."
