# Real Network Multiplayer Acceptance

This is the release-gate procedure for Issue #33. Split-screen alone is not accepted as evidence because it does not exercise a remote SMAPI peer connection.

## Test environment

- Use two independent Stardew Valley processes connected as a real host and farmhand, preferably on two computers.
- Install the exact same Evil Farm Owner build on both peers and compare the SHA-256 hash of `EvilFarmOwner.dll`.
- Use Stardew Valley 1.6.15 or later in the supported 1.6 line and SMAPI 4.0 or later.
- Use a disposable co-op save with both players on the main farm, at least one currently available adult NPC, several dry crops, several mature crops including a trellis crop when possible, placed machines that force a detour, and enough money on each player for the displayed six-hour reservation.
- Place ordinary player chests on the main farm: one with a compatible partial stack, one with the same item at another quality, one with the same category, and one nearly full fallback chest.
- Keep both SMAPI consoles visible and retain both logs.

Run `efo_netstatus` on each peer before every scenario. The host must report `role=host`; the farmhand must report `role=farmhand`; both must converge on the same non-empty session and active contract ID after acceptance.

The host diagnostic must also report `recoveryHealthy=True` and `quarantineHealthy=True`. The host persists a bounded processed-request ledger and the latest result for each requesting player at save time. A missing ledger is valid for a new save; an incompatible or internally inconsistent ledger disables new contracts instead of risking repeated world mutation. A retained cargo recovery record sets `quarantineHealthy=False` and blocks later harvest contracts until exact quarantine reconstruction succeeds.

## Scenarios

### 1. Farmhand requests one complete farm-work shift

1. Record both players' money and inventories, NPC state, dry and mature crops, ready tappers, ordinary chest contents, overflow, and `efo_netstatus` on both peers.
2. On the farmhand, press `K`, select an available NPC, choose the harvest destination, and confirm. There must be no task-selection screen.
3. Verify the farmhand sees pending then accepted feedback; the host receives one request and owns one contract, one NPC lease, and one six-hour wage reservation.
4. Verify both peers observe the same deterministic stage order: harvest ready crops and tappers, water dry crops, then sort ordinary farm chests. A stage with no work is skipped without a second hire, charge, or return journey.
5. Verify the worker prefers the right/east boundary entrance and safely falls back to another genuine boundary entrance after a bounded live-route stall. The worker must route around trellises, kegs, chests, machines, and fences without removing or changing placed objects.
6. Verify exact harvested outputs, quality, stack, and by-products reach the destination selected at confirmation. If storage becomes unavailable, verify the exact remainder is retained once through inventory, overflow, visible drop, or quarantine; it must never enter the shipping bin or disappear.
7. Verify every reachable dry crop is watered, then verify every committed sorting transfer moves one complete exact stack under both chest locks. Unrelated and excluded storage remains unchanged.
8. Run `efo_report` on both peers. Verify the aggregate action count, harvest destinations, completed/skipped chest transfers, one contract ID, and one final wage/refund agree.
9. Verify only the requesting farmhand's money changes, exactly once. The contract ID disappears from `efo_netstatus` after the single final settlement and lease restoration.

### 2. Empty stages and fail-closed transitions

1. Repeat with only watering work, only harvesting work, only chest-sorting work, and no supported work. The first three cases must run only the non-empty stage inside one shift; the no-work case must reserve no final wage and leave the NPC and world unchanged.
2. Change a planned source or destination chest during the sorting stage. Verify the whole shift stops, restores or persistently quarantines any detached stack exactly once, and does not start another stage or settlement.
3. Force day end or the hard stop during each stage. Verify completed work is included in the aggregate report, the NPC lease is restored once, and the requesting player is settled once.

### 4. Host request and simultaneous request ordering

1. Have host and farmhand open contract previews before either confirms.
2. Confirm on both peers as close together as practical.
3. Verify the host accepts exactly one request in receipt order and explicitly rejects the other as already active.
4. Verify only the accepted requester is charged and exactly one NPC lease, target mutation, and settlement occur.
5. After completion, have the host request another complete shift and verify the farmhand observes matching stage snapshots and final result.

### 5. Disconnect, reconnect, and replay safety

1. Start a farmhand complete-shift request and disconnect the farmhand after acceptance but before harvest delivery finishes.
2. Verify the host continues safe delivery/overflow and restores the NPC; no cargo is lost or duplicated.
3. Reconnect the same farmhand. Verify the active snapshot or processed final result is restored and the requesting farmer receives the correct refund despite the disconnect.
4. Repeat with a disconnect immediately after confirmation/pending feedback. Delay an old-session response and sync state until after the host reconnects. Verify the farmhand accepts only the sync state whose nonce matches its latest sync request, establishes that response's new host session, ignores both delayed old-session messages, then resends the pending request once with its original request ID. The host must return the prior request result and must not dispatch or charge twice.

### 6. Host save and restart replay safety

1. Complete a farmhand farm-work shift, record its request/contract result, then let the host save normally.
2. Quit and restart the host process, reconnect the same farmhand, and verify `efo_netstatus` shows a new host session with `recoveryHealthy=True` and a nonzero processed count.
3. Resend the exact saved request ID from the farmhand test harness. Verify the host returns the prior accepted response/result rebound to the new session, with no new lease, crop mutation, item transfer, charge, or refund.
4. Repeat with a previously rejected request and verify it remains rejected without reevaluating into a new contract.
5. In a disposable copy, corrupt or schema-mismatch the persisted recovery record. Include a duplicate transfer ID and a produced-item total which does not equal requester inventory plus chest, overflow, and visible-drop totals. Verify the host reports `recoveryHealthy=False` and rejects all new contracts without mutating money or world state.
6. In disposable copies created by protocol-3 through protocol-8 builds, retain a clean recovery ledger and load it with protocol 9. Verify the host validates and rebinds each prior response/result to the new session, reports `recoveryHealthy=True`, and an exact request replay causes no second contract, mutation, transfer, charge, or refund. A mixed or older-than-3 schema must still fail closed.
7. Start workers with both baseline and non-baseline task profiles. Verify the farmhand's start notice displays the exact multiplier from the host snapshot and rejects a missing or out-of-range multiplier without rendering an action.

### 7. Rejection safety

For each case below, verify the request is rejected with no money, NPC, crop, cargo, chest, or overflow mutation:

- insufficient requester funds;
- requester not on the main farm;
- NPC becomes unavailable after preview;
- no valid dry/mature target or no complete chest-sorting plan;
- second request while a contract is active;
- mismatched Evil Farm Owner versions on the two peers.

## Final evidence

The multiplayer gate passes only when:

- the complete harvest → watering → chest-sorting shift and every empty-stage variant pass over a real host/farmhand connection;
- both SMAPI logs contain no Evil Farm Owner error or unhandled exception;
- before/after money, crop, chest, overflow, and inventory counts reconcile exactly;
- disconnect/reconnect produces one contract, one mutation, and one settlement;
- host save/restart plus exact request replay produces no second contract, mutation, transfer, charge, or refund;
- the tested DLL SHA-256 and mod version are recorded in the Issue #33 or PR comment.
