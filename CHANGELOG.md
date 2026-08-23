# Changelog

## Unreleased

- Added confirmation for a visible named-NPC watering contract.
- Added fresh availability, funds, target, and two-way path checks before dispatch.
- Added a recoverable NPC work lease with timeout and day-end cleanup.
- Added six-hour wage reservation, per-started-hour settlement, and explicit rest-day triple-pay authorization.
- Added deterministic wage, settlement, target-ordering, and left-entrance selection logic tests.
- Added task selection between whole-farm watering and one-crop visible harvesting.
- Added vanilla-compatible harvest capture for exact quality, quantity, regrowth, metadata, and by-products.
- Added deterministic chest ranking, mutex-protected partial delivery, persistent team overflow, replay protection, and explicit emergency ground drops.
- Added `efo_overflow` to retrieve harvest results which could not fit in an eligible farm chest.
- Added host-authoritative multiplayer contract requests, bounded request replay protection, phase/cargo/transfer snapshots, reconnect synchronization, and host-only visual action messages.
- Bound wage reservation and refund to the requesting farmer instead of the host's local player.
- Added mutex-aware persistent overflow delivery and save-time lease/cargo cleanup.
- Expanded the deterministic logic harness to 21 wage, routing, protocol serialization, authorization, ordering, reconnect, stale-message, and replay tests.

## 0.1.0

- Added the first playable hired farmhand work pass.
- Added watering, harvesting, debris clearing, fertilizing, and planting tasks.
- Added config file support.
- Added Chinese and English translations.
- Added SMAPI console commands for testing.
