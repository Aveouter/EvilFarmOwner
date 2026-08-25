# Hiring UI layout specification

This specification keeps the hiring flow close to Stardew Valley's dialogue menus while leaving a repeatable worker-row region for future multi-worker selection. It intentionally describes presentation only; it does not enable concurrent contracts.

## Shared layout

- Use the vanilla dialogue box, dialogue/small fonts, text colors, cursor, portrait textures, and menu button texture.
- Keep 48–56 px horizontal content padding and at least 16 px between unrelated controls.
- Fit within the viewport with a 64 px outer margin. English and Chinese strings wrap inside their assigned content width.
- Focus order follows the visual order: worker rows from top to bottom, secondary action, previous/next, confirm/back as applicable.

## States

### Available-worker roster

```text
┌ Workers for hire ───────────────────────────────┐
│ [portrait] Leah              6 hearts   Up to 480g │
│ [portrait] Robin             4 hearts   Up to 540g │
│ ...                                                │
│ Automatic work        Previous   1 / 2      Next   │
└────────────────────────────────────────────────────┘
```

Only currently hireable adults appear. Hovered/controller-focused rows use a subtle vanilla selection tint; there are no availability explanations in the list.

### One-worker confirmation

```text
┌ Confirm farm-work shift ────────────────────────┐
│ [portrait] Leah   6 hearts                 480g max │
│ Work       All ready supported farm jobs           │
│ Delivery   Classified chests                    >   │
│ Today      Regular day                    Up to 480g │
│ Back                                      Confirm   │
└────────────────────────────────────────────────────┘
```

The worker card is a repeatable row. This reserves space for future per-worker subtotals without displaying inactive controls or promising concurrency.

### Future multi-worker confirmation (layout reservation only)

```text
│ [portrait] Leah   6 hearts                 480g max │
│ [portrait] Robin  4 hearts                 540g max │
│ ...                         Combined authorization │
```

No current interaction adds a second worker.

### Empty roster

```text
│ No one is available for hire right now.             │
│ Automatic work                              Close   │
```

### Blocking warning

```text
│ ! You need 480g available to authorize this shift.  │
│ Back                              Confirm (disabled) │
```

Warnings occupy one contextual line above the buttons and appear only when confirmation is blocked.

## Input acceptance

- Mouse: each visible worker row, delivery selector, and button has one non-overlapping hit box.
- Keyboard/controller: default focus is the first worker row on the roster and Confirm on an affordable confirmation; directional neighbors follow the screen order.
- Page changes rebuild row hit boxes and focus the first row on the new page.
- Escape/controller Back returns one level without starting work.
