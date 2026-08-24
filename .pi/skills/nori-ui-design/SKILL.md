---
name: nori-ui-design
description: >
  Design, review, and refine the Nori Desktop Pet Vue frontend. Use when modifying
  UI layouts, visual hierarchy, spacing, typography, interaction design, settings,
  chat UI, model management, onboarding, or other frontend presentation under
  app/desktop. Preserve Nori's deep-ocean, character-centered identity and avoid
  generic SaaS/dashboard aesthetics.
---

# Nori UI Design

Before making frontend design changes:

1. Read `CLAUDE.md`.
2. Read `docs/规范.md`.
3. Inspect the existing component.
4. When relevant, inspect existing tokens, UnoCSS shortcuts, theme overrides,
   and `App*` UI components.
5. Read the relevant files under `references/`.

Repository rules override this skill.

## Product direction

Nori is a character-centered desktop companion application.

The interface should feel calm, intimate, futuristic, deep-ocean inspired,
slightly mysterious, polished, and character-first.

It must not resemble a generic SaaS admin dashboard.

## Priorities

1. Nori / current character
2. user's immediate action
3. conversation and interaction
4. important application state
5. configuration and technical details

## References

- `references/nori-visual-language.md`
- `references/component-usage.md`
- `references/desktop-app-patterns.md`
- `references/motion-and-glow.md`
- `references/nori-review-checklist.md`

## Verification

From `app/desktop`:

```bash
pnpm build
pnpm test
```

Follow any additional verification requirements in `CLAUDE.md` and `docs/规范.md`.
