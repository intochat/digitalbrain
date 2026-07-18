---
version: alpha
name: TripRadar
description: >
  Design system for TripRadar — AI-powered travel planning platform.
  Shared across all user-facing applications.

colors:
  # Brand
  primary-50: "#fefce8"
  primary-400: "#facc15"
  primary-500: "#eab308"
  primary-600: "#ca8a04"
  primary-700: "#a16207"
  secondary-50: "#ecfeff"
  secondary-400: "#22d3ee"
  secondary-500: "#06b6d4"
  secondary-600: "#0891b2"
  secondary-700: "#0e7490"

  # Surfaces — Light
  surface: "#ffffff"
  surface-accent: "#f1f5f9"

  # Surfaces — Dark
  surface-dark: "#0f0f0f"
  surface-dark-secondary: "#1a1a1a"
  surface-dark-tertiary: "#242424"
  surface-accent-dark: "#2a2a2a"
  surface-accent-dark-hover: "#323232"

  # Content (text) — Light
  content: "#0f172a"
  content-secondary: "#475569"
  content-muted: "#5c6b7d"

  # Content (text) — Dark
  content-dark: "#f8f9fa"
  content-secondary-dark: "#e9ecef"
  content-muted-dark: "#adb5bd"
  content-disabled-dark: "#6c757d"

  # Borders — Light
  outline: "#cbd5e1"
  outline-secondary: "#94a3b8"

  # Borders — Dark
  outline-dark: "#404040"
  outline-secondary-dark: "#2d2d2d"
  outline-accent-dark: "#4a4a4a"

  # Interactive states — Light
  interactive: "#e2e8f0"
  interactive-active: "#ec4899"

  # Interactive states — Dark
  interactive-dark: "#404040"
  interactive-dark-hover: "#4a4a4a"
  interactive-dark-active: "#525252"
  interactive-active-dark: "#06b6d4"

  # Buttons — Light
  button: "#0f172a"
  button-text: "#ffffff"
  button-hover: "#1f2937"

  # Buttons — Dark
  button-dark: "#f8f9fa"
  button-text-dark: "#0f172a"
  button-hover-dark: "#e9ecef"

  # Semantic
  error: "#ef4444"
  success: "#22c55e"
  warning: "#f59e0b"

typography:
  headline-lg:
    fontFamily: Inter
    fontSize: 1.875rem
    fontWeight: 600
    lineHeight: 1.3
    letterSpacing: -0.02em
  headline-md:
    fontFamily: Inter
    fontSize: 1.5rem
    fontWeight: 600
    lineHeight: 1.3
    letterSpacing: -0.01em
  headline-sm:
    fontFamily: Inter
    fontSize: 1.25rem
    fontWeight: 600
    lineHeight: 1.3
  body-lg:
    fontFamily: Inter
    fontSize: 1.125rem
    fontWeight: 400
    lineHeight: 1.75
  body-md:
    fontFamily: Inter
    fontSize: 1rem
    fontWeight: 400
    lineHeight: 1.75
  body-sm:
    fontFamily: Inter
    fontSize: 0.875rem
    fontWeight: 400
    lineHeight: 1.5
  label-lg:
    fontFamily: Inter
    fontSize: 0.875rem
    fontWeight: 500
    lineHeight: 1.25
  label-md:
    fontFamily: Inter
    fontSize: 0.75rem
    fontWeight: 500
    lineHeight: 1.25
  label-sm:
    fontFamily: Inter
    fontSize: 0.6875rem
    fontWeight: 500
    lineHeight: 1.25

rounded:
  none: 0px
  sm: 4px
  md: 6px
  lg: 8px
  xl: 12px
  full: 9999px

spacing:
  xs: 4px
  sm: 8px
  md: 16px
  lg: 24px
  xl: 32px
  2xl: 48px
  3xl: 64px

components:
  button-primary:
    backgroundColor: "{colors.button}"
    textColor: "{colors.button-text}"
    rounded: "{rounded.lg}"
    padding: 10px
  button-primary-hover:
    backgroundColor: "{colors.button-hover}"
  button-primary-dark:
    backgroundColor: "{colors.button-dark}"
    textColor: "{colors.button-text-dark}"
  button-primary-dark-hover:
    backgroundColor: "{colors.button-hover-dark}"
  button-secondary:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.content}"
    rounded: "{rounded.lg}"
    padding: 10px
  button-secondary-hover:
    backgroundColor: "{colors.surface-accent}"
  button-secondary-dark:
    backgroundColor: "{colors.surface-dark}"
    textColor: "{colors.content-dark}"
  button-secondary-dark-hover:
    backgroundColor: "{colors.surface-accent-dark}"
  button-destructive:
    backgroundColor: "#dc2626"
    textColor: "#ffffff"
    rounded: "{rounded.lg}"
    padding: 10px
  button-ghost:
    backgroundColor: transparent
    textColor: "{colors.content-secondary}"
    rounded: "{rounded.lg}"
    padding: 10px
  button-ghost-hover:
    backgroundColor: "{colors.surface-accent}"
    textColor: "{colors.content}"
  card:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.lg}"
    padding: 20px
  card-dark:
    backgroundColor: "{colors.surface-dark}"
  input:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.content}"
    rounded: "{rounded.lg}"
    padding: 10px
  input-dark:
    backgroundColor: "{colors.surface-dark}"
    textColor: "{colors.content-dark}"
---

## Overview

TripRadar is an AI-powered travel planning platform. The design language is **functional minimalism** — clean surfaces, clear hierarchy, and generous whitespace. The UI should feel like a professional productivity tool (Linear, Notion) rather than a flashy consumer app.

The brand personality is **competent, calm, and trustworthy**. Interfaces should be information-dense without feeling cluttered. Every element earns its place.

The system supports **light and dark modes** as first-class citizens. Dark mode uses warm undertones (#0f0f0f base) rather than pure black, creating a comfortable reading experience for extended use.

**Target platforms:** WebUI (React + Tailwind), with future expansion to other clients. All platforms share the same token values.

## Colors

The palette is built on **neutral foundations** with two accent palettes for brand identity.

- **Primary (Yellow):** A warm amber-yellow scale (#eab308 base) used sparingly for brand accents and highlights. Not used for text or large surfaces.
- **Secondary (Cyan):** A cool cyan scale (#06b6d4 base) used for active states in dark mode and secondary interactive elements.
- **Surface:** Pure white (#ffffff) in light mode, warm near-black (#0f0f0f) in dark mode. Accent surfaces use slate-50 (#f1f5f9) light and warm gray (#2a2a2a) dark.
- **Content:** Deep slate (#0f172a) for primary text in light mode, soft white (#f8f9fa) in dark mode. Three tiers: primary, secondary (#475569 / #e9ecef), and muted (#64748b / #adb5bd).
- **Outline:** Slate borders (#cbd5e1 light, #404040 dark). Subtle enough to separate without dominating.
- **Button:** Inverted scheme — dark buttons (#0f172a) on light backgrounds, light buttons (#f8f9fa) on dark backgrounds. This creates strong contrast for primary actions.
- **Interactive:** Pink (#ec4899) for active states in light mode, cyan (#06b6d4) in dark mode.

## Typography

The type system uses **Inter** as the sole typeface across all weights and sizes. Inter was chosen for its excellent screen readability, wide language support (Latin + Cyrillic), and optical sizing features.

- **Headlines:** Inter Semi-Bold (600) with tight letter-spacing (-0.02em for large, -0.01em for medium). Used for page titles, section headers, and card titles.
- **Body:** Inter Regular (400) at 14-18px with relaxed line-height (1.5-1.75). Optimized for long-form readability in trip details, descriptions, and history items.
- **Labels:** Inter Medium (500) at 11-14px. Used for form labels, badges, metadata, timestamps, and UI controls.

Font stack fallback: Inter → ui-sans-serif → system-ui → -apple-system → BlinkMacSystemFont → Segoe UI → Roboto → Helvetica Neue → Arial → Noto Sans → sans-serif.

## Layout

The layout follows a **constrained-width** model with responsive breakpoints.

- **Max content width:** 1152px (max-w-6xl) for marketing pages, 672px (max-w-2xl) for reading-focused content like changelog.
- **Page padding:** 16px mobile → 24px tablet → 32px desktop (px-4 → sm:px-6 → lg:px-8).
- **Spacing scale:** 4px base unit. Common values: 4, 8, 16, 24, 32, 48, 64px.
- **Grid:** CSS Grid for form layouts (1-2 columns responsive). Flexbox for inline arrangements.
- **Breakpoints:** xs: 475px, sm: 640px, md: 768px, lg: 1024px, xl: 1280px.

Profile pages use a thin shell pattern: `ProfileLayout` wrapper with consistent horizontal padding and bottom padding.

## Elevation & Depth

Depth is achieved through **borders and surface color shifts** rather than shadows. This keeps the interface flat and clean.

- **Cards:** 1px border (outline/outline-dark) + surface background. No box-shadow by default.
- **Dropdowns/Popovers:** 1px border + shadow-lg for floating elements that need to stand out from the page.
- **Focus rings:** 2px ring with content/10 opacity. Subtle but visible for keyboard navigation.
- **Hover states:** Background color shift to surface-accent (light) or surface-accent-dark (dark). No elevation change.

Exception: the header uses backdrop-filter blur with semi-transparent background when scrolled, creating a frosted glass effect.

## Shapes

The shape language is **softly rounded** — modern without being bubbly.

- **Buttons, inputs, cards:** 8px border-radius (rounded-lg). The default for all interactive containers.
- **Badges, pills:** 9999px (rounded-full) for status indicators, date badges, and filter chips.
- **Dropdowns, popovers:** 8px (rounded-lg), matching their trigger elements.
- **Images:** 8px (rounded-lg) with subtle border.
- **Small elements:** 4-6px (rounded-sm to rounded-md) for inline tags and micro-UI.

## Components

### Buttons
Four variants: **primary** (dark bg, white text), **secondary** (bordered, transparent bg), **destructive** (red bg), **ghost** (no border, text only). Three sizes: sm (px-3 py-1.5 text-xs), md (px-4 py-2.5 text-sm), lg (px-5 py-3 text-sm). All buttons use rounded-lg and 150ms color transitions.

### Inputs
Single `Input` component for all text fields. Consistent border, padding, and focus ring. Dark mode inverts colors. Never use raw `<input>` with custom styling.

### Dropdowns
Portal-based popup with search support, keyboard navigation, and generic type parameter. Used for all select/option pickers — never native `<select>`.

### SearchInput
Autocomplete input with suggestion dropdown. Used for location search, airport search, and future searchable fields. Accepts generic `SearchSuggestion` items with label, secondary text, and badge.

### DatePicker
Portal-based calendar popup with month navigation, today shortcut, and min/max constraints. Used for all date selection — never `<input type="date">`.

### Cards
Rounded-lg border containers with 16-20px padding. No shadow. Content hierarchy through typography weight and color, not elevation.

### Empty States
`SectionEmpty` component with icon, message, and optional CTA button. Centered layout. Used at section level, not inside lists.

### Error States
`SectionError` component with message and retry button. Used at section level with early return pattern.

### Inline Delete Confirmation
Action buttons transform into "Delete? Yes / No" inline UI with loading spinner. No `window.confirm()` or modal dialogs for destructive actions.

## Do's and Don'ts

- Do use the design token colors from this file — never hardcode hex values in components
- Do support both light and dark modes for every new component
- Do use Inter as the only typeface — no secondary fonts
- Do use shared UI components (Button, Input, Dropdown, SearchInput, DatePicker) — never raw HTML elements with custom styling
- Do translate all user-visible text through the i18n system (en + ru)
- Do use rounded-lg (8px) as the default border radius for containers
- Do use border + surface color for depth — not box-shadow
- Don't use `window.confirm()` or `window.alert()` — use inline confirmation patterns
- Don't use arbitrary Tailwind values (text-[15px]) — stick to the type scale
- Don't mix rounded corners sizes within the same component
- Don't use more than three content color tiers (primary, secondary, muted) in a single view
- Don't add shadows to cards or containers unless they are floating overlays (dropdowns, popovers)
- Don't use primary brand colors (yellow/cyan) for text or large surfaces — they are accent-only


## Responsive Behavior

### Breakpoints

| Name | Width | Key Changes |
|------|-------|-------------|
| Mobile Small | <475px | Single column, compact padding (16px), stacked cards |
| Mobile | 475-640px | xs breakpoint, slightly wider touch targets |
| Tablet | 640-768px | sm breakpoint, 2-column form grids begin |
| Desktop Small | 768-1024px | md breakpoint, sidebar layouts, expanded padding (24px) |
| Desktop | 1024-1280px | lg breakpoint, full navigation, 32px padding |
| Large Desktop | >1280px | xl breakpoint, centered content with generous margins |

### Touch Targets

- Minimum touch target: 44px height for all interactive elements on mobile
- Buttons: sm (px-3 py-1.5), md (px-4 py-2.5), lg (px-5 py-3) — all meet 44px with text
- Icon buttons: minimum p-2 (40px total with icon), prefer p-2.5 on mobile
- Dropdown items: full-width with py-2 minimum
- SearchInput suggestions: full-width buttons with py-2 padding

### Collapsing Strategy

- Page padding: 32px → 24px → 16px (lg → sm → mobile)
- Content max-width: 1152px marketing, 672px reading content — both centered
- Form grids: 2-column → single column at sm breakpoint
- Card action buttons: horizontal row → may wrap on narrow screens
- Navigation: full horizontal → hamburger at md breakpoint
- Filter tabs: horizontal scroll with overflow-x-auto on mobile
- Typography: headline sizes reduce by one step on mobile (3xl → 2xl → xl)

## Agent Prompt Guide

### Quick Color Reference

**Light mode:**
- Background: `#ffffff`
- Card/section bg: `#f1f5f9`
- Primary text: `#0f172a`
- Secondary text: `#475569`
- Muted text: `#5c6b7d`
- Border: `#cbd5e1`
- Button bg: `#0f172a`, text: `#ffffff`
- Brand accent: `#eab308` (yellow), `#06b6d4` (cyan) — accent only, never for text

**Dark mode:**
- Background: `#0f0f0f`
- Card/section bg: `#2a2a2a`
- Primary text: `#f8f9fa`
- Secondary text: `#e9ecef`
- Muted text: `#adb5bd`
- Border: `#404040`
- Button bg: `#f8f9fa`, text: `#0f172a`
- Brand accent: `#facc15` (yellow), `#22d3ee` (cyan) — accent only

### Example Prompts for New Platforms

When building a new client (e.g., mobile app, mini app), use this prompt as a starting point:

> "Build a UI using the TripRadar design system from DESIGN.md. Use the exact color tokens, Inter font at specified weights, 8px border-radius default, and border-based depth (no shadows except on floating overlays). Support light and dark modes."

### Iteration Guide

1. Always use Inter as the only typeface — no secondary fonts, no monospace exceptions in UI
2. Default border-radius is 8px (rounded-lg) for all containers, buttons, inputs
3. Depth through borders, not shadows — only dropdowns/popovers get shadow-lg
4. Three text tiers maximum per view: primary (#0f172a), secondary (#475569), muted (#5c6b7d)
5. Dark mode is warm: #0f0f0f base, not pure black. Accent surfaces #2a2a2a, not gray
6. Brand yellow/cyan are accent-only — never for text, buttons, or large surfaces
7. All user-visible text must support en + ru through the i18n system
8. Use shared UI components (Button, Input, Dropdown, SearchInput, DatePicker) — never raw HTML
9. Inline confirmation for destructive actions — no window.confirm, no modal dialogs
10. Mobile-first: design for 375px width, then expand
