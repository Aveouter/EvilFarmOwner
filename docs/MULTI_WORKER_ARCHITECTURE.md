# Multi-worker architecture boundary

This document describes the concurrent worker runtime implemented on the draft v0.5.0 branch. It is not a claim that v0.5.0 has passed its live multiplayer release gates.

## Host-owned shift snapshot

- `MaximumConcurrentWorkers` accepts 1–4 and defaults to 1.
- Harvesting and watering can use the full configured limit. If only animal care and storage sorting are enabled, the effective selection limit is the number of those exclusive stages, so every hired NPC receives real work.
- The host captures wages, enabled stages, harvest destination, and the worker limit once at shift start. Recovery uses that immutable snapshot even if configuration changes during the shift.
- Legacy schema-10 settings and recovery records migrate to the single-worker default.

## Deterministic selection and partitioning

- Manual selection preserves the chosen NPC set; execution order is stable by internal NPC name.
- Automatic selection sorts by task efficiency, friendship-adjusted maximum wage, friendship hearts, then ordinal NPC name, while respecting the shared authorization budget.
- Every selected worker may share harvesting and watering; the live claim ledger gives each crop/resource target one owner before travel begins. Animal care and storage sorting remain exclusive and are assigned in stable order to avoid duplicate animal mutations and chest plans.
- A one-worker shift receives every enabled stage and remains the one-element form of the same runtime.

## Claim lifecycle

1. A harvest or watering target is identified by location and tile and can be claimed by exactly one worker.
2. Harvest commits its claim after the world action so another worker cannot replay the mutation.
3. Watering releases its short-lived claim after the action so a later harvest pass can inspect the same crop.
4. A failed worker releases only its uncommitted claims. Committed harvest claims remain protected.
5. Final reconciliation only considers targets that are still available to the current worker.

The host owns the claim ledger. Farmhands receive snapshots and results but never mutate contract work.

## Independent worker state and recovery

Each worker owns an independent NPC lease, stage controllers, cargo, elapsed-time settlement, runtime snapshot, and completion record. Ordered chest mutexes protect storage transfers, and a worker cannot release another worker's lease or storage ownership.

If an initial worker fails, the group performs at most one reassignment pass. Failed stages move to the first successful worker in stable name order. The recovery pass is non-billable, completed mutations remain protected by their claims and transfer identities, and the aggregate result must equal the sum of per-worker settlements.

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
- Completed request/result recovery survives save/reload and host-session rebinding. An interrupted live shift is restored safely; it is not silently replayed after a host restart.

## Remaining release gates

The PR remains Draft until one final live matrix demonstrates:

- one-worker behavior matches the released baseline;
- two to four workers divide enabled stages and do not collide at farm, building, or chest entrances;
- failed-worker reassignment does not duplicate work or wages;
- save/reload, day end, host restart, farmhand reconnect, player departure, and storage contention recover safely;
- a real host and farmhand process agree on active snapshots and final settlement.

Only after those gates pass may README and release notes advertise concurrent workers.
