# Changelog

## 0.1.0 - 2026-08-24

- Added confirmation for a visible named-NPC watering contract.
- Added fresh availability, funds, target, and two-way path checks before dispatch.
- Added a recoverable NPC work lease with timeout and day-end cleanup.
- Added six-hour wage reservation, per-started-hour settlement, and explicit rest-day triple-pay authorization.
- Added deterministic wage, settlement, target-ordering, and boundary-entrance selection logic tests.
- Added task selection between whole-farm watering and one-crop visible harvesting.
- Added vanilla-compatible harvest capture for exact quality, quantity, regrowth, metadata, and by-products.
- Added deterministic chest ranking, mutex-protected partial delivery, persistent team overflow, replay protection, and explicit emergency ground drops.
- Fixed dense-field routing by using vanilla live crop collision instead of spawn suitability; ordinary crop tiles are traversable while trellises remain blocking.
- Changed worker arrival to prefer the right/east farm entrance and use other genuine boundary entrances only as safe fallbacks.
- Added runtime entrance failover so a worker stalled on the first live step excludes that side instead of retrying every crop edge.
- Added bounded lease recovery: controller conflicts wait briefly, then release only this mod's lease and safety flags without overriding the other activity.
- Moved emergency harvest drops to the on-farm requester or a deterministic collision-free delivery tile before falling back to the worker position.
- Excluded dead crops from watering target acquisition.
- Added exact requester-inventory delivery when no chest route can accept an item and the requester remains on the farm with capacity.
- Added `efo_overflow` to retrieve harvest results which could not fit in an eligible farm chest.
- Added host-authoritative multiplayer contract requests, bounded request replay protection, phase/cargo/transfer snapshots, reconnect synchronization, and host-only visual action messages.
- Persisted the bounded processed-request ledger and latest per-player results so host restarts rebind prior transactions to the new network session without repeating work or charges.
- Bound wage reservation and refund to the requesting farmer instead of the host's local player.
- Added mutex-aware persistent overflow delivery and save-time lease/cargo cleanup.
- Expanded the deterministic logic harness to 37 wage, routing, storage, protocol serialization, authorization, ordering, reconnect, stale-message, and replay tests.
- Removed the legacy instant `efo_work`, task toggles, player-centered scan settings, and bundled user config from the production release surface.
- Corrected manifest ownership, release description, and GitHub update metadata.
- Added English and Chinese UI, configuration, multiplayer, failure, storage, and settlement text.
