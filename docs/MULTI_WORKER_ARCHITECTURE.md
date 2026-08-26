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

## Route reservation gate

Future concurrent movement uses a host-owned, shift-scoped route ledger before a controller receives a path:

- every proposed route carries worker and assignment IDs, a requested start slot, and one tile for each movement slot;
- simultaneous proposals are sorted by requested slot, worker ID, then assignment ID, so caller or network arrival order cannot choose a winner;
- a tile can have only one owner in a slot, and two workers cannot traverse one edge in opposite directions in the same slot;
- a losing proposal may shift forward only within a fixed wait budget; exhausting that budget returns a rejection instead of waiting forever;
- the host advances one monotonic committed-through slot. Elapsed reservations remain immutable history, while worker failure releases only that worker's future tiles and edges;
- the existing single-worker route is the one-proposal case and receives its requested start slot without delay.

The deterministic ledger is implemented and covered by pure tests, but is not yet wired to live NPC controllers. Concurrent dispatch remains disabled until controller integration, save/reconnect representation, and multi-worker runtime acceptance are complete.
