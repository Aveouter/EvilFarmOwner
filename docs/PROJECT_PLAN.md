# Project Plan

This document defines the issue taxonomy, milestone structure, and development order for Evil Farm Owner.

## Issue Taxonomy

Every issue should have one label from each group when possible.

### Type

- `type:feature`: user-facing behavior or feature.
- `type:bug`: confirmed defect or unsafe behavior.
- `type:design`: gameplay, UX, or architecture decision before implementation.
- `type:tech-debt`: internal cleanup or structure work.
- `type:docs`: user docs, contributor docs, templates, or project process.
- `type:idea`: unscheduled future concept.

### Priority

- `priority:p0`: save corruption, data loss, or release blocker.
- `priority:p1`: current milestone priority.
- `priority:p2`: important but not blocking the current milestone.
- `priority:p3`: later exploration.

### Area

- `area:ui`: menus, HUD, input flow, and player interaction.
- `area:farm-work`: watering, harvesting, planting, fertilizing, debris cleanup.
- `area:storage`: chests, warehouse, routing, sorting, inventory movement.
- `area:multiplayer`: host/client behavior and sync.
- `area:npc`: worker identity, visuals, movement, and NPC-like behavior.
- `area:config`: settings and persistence.
- `area:docs`: README and contributor-facing documentation.
- `area:release`: packaging, versioning, and release workflow.

### Status

- `status:needs-design`: not ready to code yet.
- `status:ready`: ready for implementation.
- `status:blocked`: blocked by another decision, issue, or external constraint.

## Milestones

### v0.1.x - Prototype Stabilization

Stabilize the current playable prototype before deeper feature work.

Focus:

- Make unsafe behavior explicit.
- Document current limitations.
- Keep host-only multiplayer behavior clear.
- Improve packaging and release hygiene.

### v0.2.0 - Hiring Menu MVP

Turn the prototype into a player-facing interaction.

Focus:

- Add a hiring menu.
- Show enabled jobs.
- Show wage before confirmation.
- Let players confirm or cancel.
- Preserve console commands for testing.

### v0.3.0 - Worker Identity & Feedback

Make farmhands feel like hired workers instead of invisible scripts.

Focus:

- Worker names or contracts.
- Work result messages.
- Better feedback for unavailable work.
- First version of worker presence or simple visuals.

### v0.4.0 - Storage & Warehouse Jobs

Add storage automation.

Focus:

- Define harvest output routing.
- Add chest sorting rules.
- Add warehouse or storage anchor design.
- Move items safely without losing stacks.

### v0.5.0 - Multiplayer Support

Make multiplayer behavior explicit and reliable.

Focus:

- Host-authoritative work execution.
- Client work requests.
- Sync user-facing messages.
- Avoid duplicate work passes.

## Current Priority

The next implementation milestone is `v0.2.0 - Hiring Menu MVP`. Before starting it, complete the project setup work in this PR so future changes have consistent issue and PR structure.
