# Multi-worker architecture boundary

The complete shift remains limited to one active worker for now. New work should nevertheless use the model below so concurrent hiring can be enabled later without changing target ownership or billing rules.

## Stable work and deterministic scheduling

- A target is identified by domain, location, and a source-specific stable ID. Coordinates alone are insufficient for animals, buildings, and movable objects.
- The host sorts targets and workers by ordinal stable IDs, then assigns each target to the worker with the lowest projected efficiency-adjusted load. Ties use worker ID.
- A one-worker shift uses the same partitioner and is therefore the one-element form of the future system.

## Claim lifecycle

1. An available target can be claimed by exactly one worker.
2. A successful world mutation commits the claim before later storage or presentation work.
3. If a worker leaves or fails, only that worker's uncommitted claims are released.
4. Committed claims remain in the ledger and must never be replayed.
5. Final reconciliation creates or claims only targets that are still available.

The host owns this ledger. Multiplayer messages will carry shift, assignment, worker, target, and sequence identities; peers render snapshots but do not mutate work state.

## Independent worker state

Each assignment will own its NPC lease, route controller, tool/cargo state, availability result, wage authorization, elapsed-time settlement, and recovery path. Storage remains protected by the existing ordered chest locks. A worker failure cannot release another worker's lock or lease.

The shift report contains per-worker authorization and charge records plus a checked aggregate total. Concurrent dispatch stays disabled until runtime save/reconnect, route collision, storage lock, and settlement coverage is complete.
