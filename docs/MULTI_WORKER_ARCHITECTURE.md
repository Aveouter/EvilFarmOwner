# Multi-worker architecture boundary

This document describes the concurrent worker runtime released in v0.5.0. It is not a claim that the waived live multiplayer matrix passed.

## Host-owned shift snapshot

- `MaximumConcurrentWorkers` accepts 1–4 and defaults to 1.
- Harvesting and watering can use the full configured limit. If only animal care and storage sorting are enabled, the effective selection limit is the number of those exclusive stages, so every hired NPC receives real work.
- The host captures wages, enabled stages, harvest destination, and the worker limit once at shift start. Recovery uses that immutable snapshot even if configuration changes during the shift.
- Legacy schema-10 settings and recovery records migrate to the single-worker default.

## Deterministic selection and partitioning

- Manual selection preserves the chosen NPC set; the host resolves aliases/casing and executes in stable canonical internal-NPC-name order.
- Automatic selection sorts by task efficiency, friendship-adjusted maximum wage, friendship hearts, then ordinal NPC name, while respecting the shared authorization budget.
- Every selected worker may share harvesting and watering; the live claim ledger gives each crop/resource target one owner before travel begins. Animal care and storage sorting remain exclusive and are assigned in stable order to avoid duplicate animal mutations and chest plans.
- A one-worker shift receives every enabled stage and remains the one-element form of the same runtime.
- A selected worker who finds no unclaimed target stays as a zero-hour standby instead of disappearing from the group. A failed stage prefers a standby in stable name order, then a worker who completed their own assignment.

## Claim lifecycle

1. A harvest or watering target is identified by location and tile and can be claimed by exactly one worker.
2. Harvest commits its claim after the world action so another worker cannot replay the mutation.
3. Watering releases its short-lived claim after the action so a later harvest pass can inspect the same crop.
4. A failed worker releases only its uncommitted claims. Committed harvest claims remain protected.
5. Final reconciliation only considers targets that are still available to the current worker.

The host owns the claim ledger. Farmhands receive snapshots and results but never mutate contract work.

## Independent worker state and recovery

Each worker owns an independent NPC lease, stage controllers, cargo, elapsed-time settlement, runtime snapshot, and completion record. Ordered chest mutexes protect storage transfers, and a worker cannot release another worker's lease or storage ownership.

If an initial worker fails, the group performs at most one reassignment pass. Failed stages prefer an idle standby in stable name order, then a successful worker. A standby is billed once when recovery work starts; an already-paid worker is not charged again. Completed mutations remain protected by their claims and transfer identities, and the aggregate result must equal the sum of per-worker settlements.

No wage reservation is charged until a worker starts at least one stage. An idle standby therefore has zero charge and zero refund. If that standby later accepts recovery work, it receives one normal billable reservation; a worker who already completed paid work can recover without a second charge.

Completed storage-transfer reports from initial and recovery controllers are reindexed into one contiguous group sequence. A successful recovery removes superseded skipped reports, while item quantities and stable transfer IDs remain unchanged.

If group startup fails after one or more workers were charged, every started shift is cancelled and its complete reservation is returned before the request fails.

## Live route reservation

The shift owns one host-side route ledger shared by all worker controllers:

- each path is expanded into map-scoped movement slots and reserved before the NPC starts moving;
- tile conflicts and opposite traversal of the same edge are serialized;
- the losing worker waits within a fixed budget, with its NPC movement paused;
- intentional reservation waits do not consume route timeout or progress-watchdog budgets;
- replacing or finishing a route releases only that worker's future reservations;
- occupied leased-worker tiles are excluded when building farm and building-entrance paths;
- farm routes retain the existing non-destructive obstacle policy and gate opening behavior;
- chest access remains additionally protected by the existing network mutex.

The deterministic route ledger, single-worker equivalence, tile conflicts, opposing edges, bounded waits, and worker-specific release are covered by the standalone Core tests.

## Protocol and persistence

- Protocol schema 11 carries worker lists, per-worker snapshots, tombstones for workers that finish early, and per-worker settlement results.
- Schema 10 is accepted only as the legacy single-worker representation.
- Automatic-contract schema 3 stores the prior selected worker list and migrates older single-worker records.
- A farmhand keeps the original pending request ID across response timeout, disconnect, and nonce-matched resynchronization. A synchronized active snapshot or result consumes it; otherwise the same request is resent and the host replay ledger prevents a second dispatch or charge.
- Completed request/result recovery survives save/reload and host-session rebinding. An interrupted live shift is restored safely; it is not silently replayed after a host restart.

## Maintainer acceptance decision

On 2026-08-27, the maintainer explicitly accepted the initial merge without running the final live matrix or a real two-process game. The deterministic suite, production build, protocol checks, source validation, and evidence tooling passed; the following scenarios remain unverified rather than passed:

- one-worker behavior matches the released baseline;
- two to four workers divide enabled stages and do not collide at farm, building, or chest entrances;
- failed-worker reassignment does not duplicate work or wages;
- save/reload, day end, host restart, farmhand reconnect, player departure, and storage contention recover safely;
- a real host and farmhand process agree on active snapshots and final settlement.

Release notes may describe the implemented concurrent-worker feature, but must retain this live-validation limitation until recorded evidence exists.
