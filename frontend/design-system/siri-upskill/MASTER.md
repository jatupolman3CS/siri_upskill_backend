# Design System Master File

> **LOGIC:** When building a specific page, first check `design-system/pages/[page-name].md`.
> If that file exists, its rules **override** this Master file.
> If not, strictly follow the rules below.

---

**Project:** siri-upskill
**Decided:** 2026-08-17 (P0-31)
**Category:** Two-sided course marketplace (learner storefront + payment/checkout + instructor/admin dashboards)
**Design Dials:** Variance 4/10 (Balanced / Modern) | Motion 3/10 (Subtle) | Density 4/10 (Standard, dashboards may override higher)

> **Note on how this file was produced:** the `ui-ux-pro-max` skill's automatic
> `--design-system` generator was run twice and both times matched this
> product to a K-12/"kids learning" or "youth/gaming" profile (Claymorphism +
> Comic Neue / Poppins; Vibrant & Block-based) because of surface keyword
> overlap with "course" / "e-learning" / "marketplace" in its product
> dataset. Neither fits a platform whose core promise is trust (payment,
> DRM-protected video) for adult professionals. The values below were
> assembled by hand from the same underlying dataset (`style`, `color`,
> `typography`, `product` domain searches run individually and cross-checked
> against real precedent products — see reasoning per section) rather than
> the auto-picker's product-type shortcut. If re-running `--design-system`
> for a future page, sanity-check its output against this file before
> trusting it wholesale.

---

## Global Rules

### Color Palette

| Role | Hex | CSS Variable | Used for |
|------|-----|--------------|----------|
| Primary | `#0369A1` | `--color-primary` | CTA buttons, links, active nav, checkout affordances |
| Primary Hover | `#075985` | `--color-primary-hover` | — |
| On Primary | `#FFFFFF` | `--color-primary-foreground` | — |
| Accent | `#B45309` | `--color-accent` | Certificates, badges, "featured" — achievement moments only, never a dominant color |
| On Accent | `#FFFFFF` | `--color-accent-foreground` | — |
| Background | `#FFFFFF` | `--color-bg` | — |
| Surface | `#F8FAFC` | `--color-surface` | Cards, panels |
| Surface Muted | `#F1F5F9` | `--color-surface-muted` | — |
| Text | `#0F172A` | `--color-text` | — |
| Text Muted | `#475569` | `--color-text-muted` | — |
| Border | `#E2E8F0` | `--color-border` | — |
| Success | `#16A34A` | `--color-success` | — |
| Warning | `#D97706` | `--color-warning` | — |
| Danger | `#DC2626` | `--color-danger` | — |
| Focus Ring | `#0369A1` | `--color-focus-ring` | — |

**Color reasoning:** searched the color domain for "trust confident growth achievement education professional" and cross-checked 8 real product palettes (Micro-Credentials/Badges, LMS, Online Course, B2B Service, CRM, Invoice/Billing, Government Portal, Real Estate). Every payment/credential-adjacent product in the dataset converges on **blue for trust** (government, fintech, CRM, billing all use it) — this is not a stylistic preference, it's the semantic the target audience already associates with "safe to pay here." **Gold/amber as a reserved accent** comes specifically from the Micro-Credentials/Badges Platform entry ("trust blue + achievement gold") — apt for a platform whose product literally includes certificates (LX-05) and whose name is "UpSkill." Full palette lives in `frontend/src/app/styles/tokens.css`, including the dark-mode overrides (not duplicated here — the token file is the source of truth, this table is the rationale).

### Typography

- **Family:** IBM Plex Sans Thai (single family for both heading and body — vary weight, not typeface)
- **Weights loaded:** 400, 500, 600, 700
- **Mood:** clean, modern, professional, readable, full Thai + Latin coverage
- **Google Fonts:** [IBM Plex Sans Thai](https://fonts.googleapis.com/css2?family=IBM+Plex+Sans+Thai:wght@400;500;600;700&display=swap) — loaded via `<link>` in `src/index.html`, not `@import`, for earlier discovery/preconnect
- **Why one family, not a heading/body pair:** this app ships a bilingual TH/EN interface (`core/i18n/`). Pairing a Latin-only display font (Poppins, DM Sans, etc.) with a separate Thai body font means every heading silently reverts to a different, unpaired fallback the moment it renders Thai text — the two scripts would visibly not belong to the same design. IBM Plex Sans Thai avoids that failure mode entirely and is this codebase's own pre-existing top recommendation (`.claude/rules/ui-design.md`).
- **Verify before shipping any real Thai copy:** line-height at body size (16px) with real Thai text containing tone marks/vowels above and below the baseline — the placeholder token comment this file replaces flagged this as unverified; still true, do it once real Thai content exists.

### Spacing Variables

*Density: 4/10 — Standard (dashboards/data tables may use a denser scale locally — see `pages/` overrides when those pages are built)*

| Token | Value | Usage |
|-------|-------|-------|
| `--space-xs` | `4px` / `0.25rem` | Tight gaps |
| `--space-sm` | `8px` / `0.5rem` | Icon gaps, inline spacing |
| `--space-md` | `16px` / `1rem` | Standard padding |
| `--space-lg` | `24px` / `1.5rem` | Section padding |
| `--space-xl` | `32px` / `2rem` | Large gaps |
| `--space-2xl` | `48px` / `3rem` | Section margins |
| `--space-3xl` | `64px` / `4rem` | Hero padding |

### Shadow Depths

| Level | Token | Usage |
|-------|-------|-------|
| Subtle lift | `--shadow-sm` | Inputs, small controls |
| Cards, buttons | `--shadow-md` | Default card elevation |
| Modals, dropdowns | `--shadow-lg` | Overlays |

Restrained on purpose — see Style Guidelines below. No neumorphic double-shadows, no glassmorphic blur-behind-content; shadows communicate elevation, not decoration.

---

## Style Guidelines

**Style:** Flat Design, with Minimalism & Swiss Style-influenced spacing/grid discipline

**Keywords:** Clean, functional, generous white space, high contrast, limited palette, grid-based, no gradients/glassmorphism/claymorphism/neumorphism

**Why this over the alternatives the dataset offered:** the product has two very different surfaces that both need the *same* style to hold up — a consumer storefront + checkout (needs to read as trustworthy and calm, not playful) and instructor/admin dashboards (needs to be data-dense and legible). Flat Design is explicitly tagged "Best For: SaaS, dashboards, corporate" and Minimalism is explicitly tagged "Best For: Enterprise apps, dashboards, SaaS platforms, professional tools" — both fit both surfaces. Claymorphism (chunky/toy-like), Glassmorphism (blur, harder to keep accessible), Vibrant & Block-based (youth/gaming-coded), and Aurora UI (decorative gradients) were all considered and rejected — none fit a payment-and-credential product for working adults.

**Icons:** SVG only (Phosphor or Lucide, per `.claude/rules/ui-design.md`) — never emoji.

### Page Pattern (marketing/landing pages)

**Pattern Name:** Hero + Feature Showcase + Social Proof + CTA

- **Conversion strategy:** lead with the value proposition (learn a skill, build a career), show concrete course/category breadth, then social proof (learner counts, instructor credibility, ratings) before the CTA — social proof matters more here than a generic SaaS pitch because the purchase is a personal, often skeptical decision ("will this course actually help me").
- **CTA placement:** hero (primary) + repeated after social proof (secondary conversion point)
- **Section order:** Hero → category/value overview → featured courses → social proof (ratings/testimonials) → CTA

This pattern is a starting point for the home/landing page (P1) — `learn`/`instructor`/`admin` surfaces are dashboards, not landing pages, and should NOT follow this pattern; they follow standard dashboard layout conventions (sidebar nav + content area, per the existing `layouts/` scaffolding).

---

## Motion

**Guideline:** subtle, functional motion only (Motion dial 3/10) — this is a trust-sensitive product, not an entertainment one. Fades and small (8–16px) position shifts on scroll-reveal and state changes; no bouncy/overshoot easing, no decorative looping animation.

```js
gsap.from(el, { opacity: 0, y: 12, duration: 0.35, ease: 'power1.out', scrollTrigger: { trigger: el, start: 'top 90%', toggleActions: 'play none none reverse' } });
```

- Requires `ScrollTrigger` registered once via `gsap.registerPlugin(ScrollTrigger)`.
- Always gate with `matchMedia('(prefers-reduced-motion: reduce)')` — skip non-essential motion and render the final state immediately.
- Keep the `y` offset small (8–16px) so it reads as a fade, not a slide.
- Don't reveal below-the-fold SEO-relevant content as invisible-by-default without a no-JS fallback (this app is SSR — content must be in the initial render regardless of animation state).

---

## Anti-Patterns (Do NOT Use)

- ❌ Low trust signals (anything that makes checkout/payment feel uncertain)
- ❌ Confusing layout
- ❌ Emojis as icons — use SVG (Phosphor/Lucide)
- ❌ Missing `cursor: pointer` on clickable elements
- ❌ Layout-shifting hovers (avoid scale transforms that shift surrounding content)
- ❌ Low contrast text — 4.5:1 minimum
- ❌ Instant state changes — always transition (150–300ms)
- ❌ Invisible focus states
- ❌ Playful/bouncy easing (`back.out`, elastic) on informational or transactional UI — reserve for nothing in this product; it reads as sloppy on payment/credential flows
- ❌ Glassmorphism, claymorphism, neumorphism, decorative gradients — inconsistent with the chosen style and harder to keep accessible

---

## Pre-Delivery Checklist

Before delivering any UI code, verify:

- [ ] No emojis used as icons (Phosphor/Lucide SVG only)
- [ ] `cursor-pointer` on all clickable elements
- [ ] Hover states with smooth transitions (150–300ms)
- [ ] Light mode: text contrast 4.5:1 minimum (verify against the real token values, not assumed)
- [ ] Dark mode: same contrast check against the dark token overrides
- [ ] Focus states visible for keyboard navigation, ≥3:1 against both light and dark surfaces
- [ ] `prefers-reduced-motion` respected
- [ ] Responsive: 375px, 768px, 1024px, 1440px — no horizontal scroll on mobile
- [ ] No content hidden behind fixed navbars
- [ ] Colors/spacing/radius/shadows referenced via `var(--token)`, never raw hex or ad-hoc pixel values
