# Component Usage

Reuse existing Nori primitives before creating new ones.

Prefer:
- AppButton
- AppCard
- AppChip
- existing App* components
- UnoCSS shortcuts
- semantic design tokens

Do not introduce:
- Tailwind CSS
- a second UI framework
- a parallel token system

## Cards

Before adding a card, ask whether spacing and alignment are enough.
Avoid nested cards unless they represent a real hierarchy boundary.

## Chips

Use chips for concise state that benefits from scanning.
Do not turn ordinary metadata into chips.
If many chips appear together, consider quieter secondary text.

## Naive UI

Follow the repository's existing theme-override mechanism.
Do not fight runtime component styling with brittle utility-class overrides.
