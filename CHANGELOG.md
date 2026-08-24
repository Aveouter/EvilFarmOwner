# Changelog

## Unreleased

- Added a manually confirmed named-NPC chest-sorting contract for ordinary player-owned main-farm chests.
- Preflight the immutable whole-stack plan, exact capacity, every source/destination route, and safe return before reserving wages or leasing the worker.
- Execute deterministic exact-stack, same-item, exact-category, and empty-chest transfers under ordered dual mutexes with exact rollback or persistent quarantine recovery.
- Force any verified detached sorting stack into the private team quarantine at the save boundary when its serializable recovery record is temporarily unavailable.
- Added bilingual task selection, contract settlement HUDs, host-authoritative protocol 7 snapshots/results, and persistent per-transfer source/destination reports.
- Isolated a crop after its live interaction routes are exhausted instead of marking every remaining crop unreachable; three independently stalled crops at one origin still trigger a bounded safe return.
- Kept recurring automatic chest sorting disabled until the manual contract passes live acceptance.

## 0.1.0-beta.1 - 2026-08-24

- Published the first explicitly non-stable test build under the MIT License.
- Marked real remote multiplayer and forced storage-recovery scenarios as unverified release-test gates rather than claiming they passed.

- Added confirmation for a visible named-NPC watering contract.
- Added fresh availability, funds, target, and two-way path checks before dispatch.
- Added a recoverable NPC work lease with timeout and day-end cleanup.
- Added six-hour wage reservation, per-started-hour settlement, and explicit rest-day triple-pay authorization.
- Added explicit background-based watering and harvesting efficiency profiles for all 27 supported workers, with conservative `1.00x` fallback, immutable host snapshots, and action-duration-only effects.
- Added one save-specific host-owned automatic contract with fixed or explicitly approved substitute pools, deterministic daily selection, hard wage caps, opt-in rest-day triple pay, and pause/resume/replace/delete management.
- Added deterministic wage, settlement, target-ordering, and boundary-entrance selection logic tests.
- Added task selection between whole-farm watering and one-crop visible harvesting.
- Added vanilla-compatible harvest capture for exact quality, quantity, regrowth, metadata, and by-products.
- Fixed regrowing crop capture by treating `Crop.harvest`'s return value as the crop-removal signal while using captured items as the success source of truth.
- Added deterministic, content-based chest classification with exact-stack, same-item, exact game-category, and empty-chest tiers.
- Ranked category chests by content purity and matching slots, then stable chest tile; NPC position only chooses the interaction edge and cannot make a category jump between otherwise equal chests.
- Required one candidate chest to accept the complete stack; partial multi-chest delivery is not used.
- Added an empty-chest fallback that chooses the greatest acceptable capacity and then stable tile order.
- Fixed dense-field routing by using vanilla live crop collision instead of spawn suitability; ordinary crop tiles are traversable while trellises remain blocking.
- Changed worker arrival to prefer the right/east farm entrance and use other genuine boundary entrances only as safe fallbacks.
- Added runtime entrance failover so a worker stalled on the first live step excludes that side instead of retrying every crop edge.
- Protected active vanilla route animations, square-walk activities, sprite animations, and movement pauses from hiring without treating persisted route-end metadata as an active activity; unavailable NPCs are omitted from the roster.
- Reworked the worker roster into compact rows that show friendship, today's hourly wage, and the six-hour maximum without redundant availability explanations or disabled footer controls.
- Added an NPC-bounds first-pixel entrance probe so false-positive arrival tiles are rejected before wages or NPC state change.
- Bounded target and chest replanning to three consecutive failures from one origin, with first-step probes on every dynamic route and lossless delivery fallback after exhaustion.
- Added bounded lease recovery: controller conflicts wait briefly, then release only this mod's lease and safety flags without overriding the other activity.
- Moved emergency harvest drops to the on-farm requester or a deterministic collision-free delivery tile before falling back to the worker position.
- Excluded dead crops from watering target acquisition.
- Stop harvest contracts when no reachable category-compatible chest can accept the complete stack; already harvested cargo enters the lossless emergency path before the worker returns.
- Added `efo_overflow` to retrieve already harvested cargo preserved after a storage-triggered stop.
- Added host-authoritative multiplayer contract requests, bounded request replay protection, phase/cargo/transfer snapshots, reconnect synchronization, and host-only visual action messages.
- Persisted the bounded processed-request ledger and latest per-player results so host restarts rebind prior transactions to the new network session without repeating work or charges.
- Added read-only `efo_report` output for the current player's latest authoritative contract result, including grouped cargo, destinations, and billing.
- Rejected internally inconsistent multiplayer recovery results with duplicate transfer IDs, unbalanced item destinations, impossible hours, or contradictory success reasons.
- Required a nonce-correlated authoritative sync-state handshake before a farmhand binds a new host session or resends a pending request, so delayed responses or sync states from the prior host session cannot capture reconnect state; clean protocol-3 through protocol-5 recovery ledgers are fully validated before being rebound to protocol 6.
- Bound wage reservation and refund to the requesting farmer instead of the host's local player.
- Added mutex-aware persistent overflow delivery and save-time lease/cargo cleanup.
- Added a separate persistent emergency cargo quarantine, idempotent transfer markers, a size-bounded host recovery record, and `efo_quarantine` retrieval so failed overflow and ground-drop operations cannot release the only owned item instances.
- Required day end, ordinary saving, and initial save creation to verify cargo ownership and force any transient remainder into the private team quarantine before contract settlement.
- Added a compile-gated, non-distributable storage fault-injection harness for live overflow, drop, quarantine, recovery-record, and terminal-write acceptance tests; the normal Release verifier rejects any DLL which exposes its command.
- Expanded the deterministic logic harness to 60 wage, efficiency, recurring-contract, availability, routing, storage, reporting, quarantine recovery, acceptance-control, protocol serialization, authorization, ordering, reconnect, stale-message, and replay tests.
- Removed the legacy instant `efo_work`, task toggles, player-centered scan settings, and bundled user config from the production release surface.
- Corrected manifest ownership, release description, and GitHub update metadata.
- Added English and Chinese UI, configuration, multiplayer, failure, storage, and settlement text.
