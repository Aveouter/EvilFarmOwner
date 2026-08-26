# Hiring UI layout specification

This specification describes the hiring flow implemented on the draft v0.5.0 branch. The current public release still defaults to one worker; the host may raise the draft setting to four after the live release gates pass.

## Shared layout

- Use the vanilla dialogue box, dialogue/small fonts, text colors, cursor, portrait textures, and menu button texture.
- Keep 48–56 px horizontal content padding and at least 16 px between unrelated controls.
- Fit within the viewport with a 64 px outer margin. English and Chinese strings wrap inside their assigned content width.
- Focus order follows the visual order: worker rows from top to bottom, secondary action, previous/next, confirm/back as applicable.

## States

### Available-worker roster

```text
┌ Workers for hire ───────────────────────────────┐
│ [✓ portrait] Leah            6 hearts   Up to 480g │
│ [  portrait] Robin           4 hearts   Up to 540g │
│ ...                                                │
│ Auto-select  Review shift   Previous  1 / 2  Next  │
└────────────────────────────────────────────────────┘
```

Only currently hireable adults appear. The top-right summary shows `Selected n / limit`. Selected rows use a pale green vanilla-style tint and check mark; hover/controller focus uses the usual parchment tint. There are no unavailable rows or availability explanations in the list.

### Shift confirmation

```text
┌ Confirm farm-work shift ────────────────────────┐
│ [portrait] Leah   6 hearts                 480g max │
│ Work       All ready supported farm jobs           │
│ Delivery   Classified chests                    >   │
│ Today      Regular day                    Up to 480g │
│ Back                                      Confirm   │
└────────────────────────────────────────────────────┘
```

With one worker the card shows portrait, friendship, and wage. With multiple workers it shows the worker count, names, and combined authorization without adding technical scheduling text.

### Multi-worker confirmation

```text
│ 3 workers                           Up to 1,560g │
│ Leah · Robin · Alex                              │
│ Work       All enabled farm jobs                 │
│ Delivery   Classified chests                  >  │
│ Back                                    Confirm │
```

The effective selection limit is host-owned. Harvesting or watering may use the full configured limit; if only animal care and storage sorting are enabled, the UI lowers the limit so every selected worker receives a stage.

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
