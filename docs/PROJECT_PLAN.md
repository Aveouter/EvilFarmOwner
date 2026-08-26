# Project Plan

This document records the active release order and the repository's issue-first development rules.

## Development workflow

Every implementation starts from an issue with one `type`, `priority`, `area`, and `status` label. Work branches from current `main`, lands through a focused pull request, passes repository checks and review, and is squash-merged. Release candidates additionally require the clean local verifier because hosted CI cannot compile against proprietary Stardew Valley assemblies.

## Released baseline

- `v0.3.0` provides complete single-worker shifts, configurable wages and stages, batched lossless harvest delivery, animal care, chest sorting, and host-authoritative protocol 9 behavior.
- `v0.3.1` is the narrow harvest-delivery hotfix: vanilla-compatible 12-slot accounting plus detailed classified-chest delivery recovery.

## Current release: v0.3.2 - Route reliability

`v0.3.2` is a single-worker stability release. It does not change the multiplayer protocol, save schema, hiring flow, or worker limit.

Completed implementation gates:

- shared route interruption classification and diagnostic snapshots;
- location-scoped blocked tiles and directed edges;
- three-attempt bounded replanning for harvest, watering, animal care, and chest sorting;
- non-destructive farm routing with gate opening;
- explicit target-skip, safe-return, and lossless cargo recovery policies;
- a standalone .NET 8 Core project with all deterministic tests running on Ubuntu CI.

Release gates still requiring recorded evidence:

- disposable-save route matrix covering dynamic obstacles, dense/trellis crops, animal-house doors, player map changes, sorting routes, and return travel;
- forced storage recovery matrix covering overflow, visible drop, quarantine, recovery records, save, and reload;
- clean production package, allowlist, command scan, SHA-256 re-download audit, and exact-ZIP SMAPI load smoke test.

## Next release: v0.5.0 - Concurrent workers

Concurrent execution remains disabled by default and the current limit remains one worker. `v0.5.0` will add a host-owned `MaximumConcurrentWorkers` setting with a range of 1–4 and default 1, stable automatic hiring, independent worker leases/routes/cargo/settlement, task claims and reassignment, route and entrance reservations, chest locks, protocol migration, and old-save compatibility.

The release gate includes deterministic 1–4 worker behavior, one-worker equivalence, save/reload, day end, host restart, farmhand reconnect, storage contention, and a real two-process host/farmhand acceptance run. Until that gate passes, documentation must not claim concurrent or fully verified remote multiplayer support.

## Deferred ideas

Debris clearing, planting, fertilizing, automatic machine refilling, automatic shipping, special/modded storage, and destructive outside-farm obstacle escalation remain outside the current release sequence and require separate design issues.
