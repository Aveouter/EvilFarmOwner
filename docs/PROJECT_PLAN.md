# Project Plan

This document records the active release order and the repository's issue-first development rules.

## Development workflow

Every implementation starts from an issue with one `type`, `priority`, `area`, and `status` label. Work branches from current `main`, lands through a focused pull request, passes repository checks and review, and is squash-merged. Release candidates additionally require the clean local verifier because hosted CI cannot compile against proprietary Stardew Valley assemblies.

## Released baseline

- `v0.3.0` provides complete single-worker shifts, configurable wages and stages, batched lossless harvest delivery, animal care, chest sorting, and host-authoritative protocol 9 behavior.
- `v0.3.1` is the narrow harvest-delivery hotfix: vanilla-compatible 12-slot accounting plus detailed classified-chest delivery recovery.
- `v0.3.2` unifies route interruption diagnostics and bounded non-destructive replanning, and moves deterministic logic tests into the standalone .NET 8 Core project used by CI.

## Current development: v0.5.0 - Concurrent workers

Concurrent execution remains opt-in: the host-owned `MaximumConcurrentWorkers` setting accepts 1–4 and defaults to 1. A shift keeps an immutable settings snapshot, and old single-worker settings migrate to the default limit of one.

Implemented on the draft feature branch:

- manual selection and stable budget-aware automatic selection for up to four available adult NPCs;
- host validation, immutable settings synchronization, protocol migration, and per-worker reconnect snapshots;
- pending farmhand requests retain their original idempotency key across timeout/disconnect resynchronization;
- independent worker leases, stage controllers, cargo state, wage settlement, and aggregate result conservation checks;
- parallel harvest/watering with target claims, exclusive animal/storage assignment, occupied-worker route avoidance, existing non-destructive replanning, gate handling, and mutex-backed chest access;
- live crop/resource claims plus deterministic same-tile, opposing-edge, entrance, and interior route reservations with bounded waiting;
- one bounded reassignment pass for failed stages, with no second wage charge;
- automatic-contract save migration and multiworker selection within the saved total authorization cap.

Automated acceptance evidence:

- the standalone Core suite already records deterministic selection, partition, claims, route conflicts, one-worker route equivalence, settlement aggregation, protocol migration, restart recovery data, and reconnect serialization;
- the production Mod builds without warnings or errors, and the current Source validation workflow passes;
- the release verifier, package allowlist, production-command scan, and SHA-256 audit remain required when the v0.5.0 release package is produced.

Maintainer acceptance decision (2026-08-27): the live 1–4 worker matrix, save/reload and reconnect scenarios, real two-process host/farmhand run, and exact-ZIP SMAPI smoke test were explicitly waived for merge without being performed. This is risk acceptance, not evidence that those scenarios passed. User-facing release notes must identify remote multiplayer and live multiworker behavior as unverified until a future recorded run supplies that evidence.

## Deferred ideas

Debris clearing, planting, fertilizing, automatic machine refilling, automatic shipping, special/modded storage, and destructive outside-farm obstacle escalation remain outside the current release sequence and require separate design issues.
