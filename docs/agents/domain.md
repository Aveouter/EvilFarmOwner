# Domain Docs

This repo uses a single-context domain documentation layout.

## Before Exploring

When working on a code change, read these files if they exist and are relevant:

- `CONTEXT.md` at the repo root for project vocabulary and domain rules.
- `docs/adr/` for architecture decisions that touch the area being changed.

If these files do not exist, proceed with the repository's existing docs and code. Do not invent missing decisions.

## Layout

```text
/
|-- CONTEXT.md
|-- docs/
|   |-- adr/
|   `-- agents/
`-- src/
```

## Usage Rules

Use terms from `CONTEXT.md` in issue titles, PR descriptions, tests, and code names when those terms exist.

If a proposed change contradicts an ADR, call that out explicitly before implementing.
