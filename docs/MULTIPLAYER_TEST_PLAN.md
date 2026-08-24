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

### 1. Farmhand requests watering

1. Record host money, farmhand money, dry-crop count, NPC location/state, and `efo_netstatus` on both peers.
2. On the farmhand, press `K`, select an available NPC, choose watering, and confirm.
3. Verify the farmhand sees pending then accepted feedback; the host receives the request and owns the contract.
4. With the right/east boundary route open, verify the named NPC enters there and returns there. Then create a setup where right-side static preflight succeeds but the first live controller step stalls; verify the host excludes that whole side, switches once to the next genuine boundary entrance, and broadcasts the same arrival tile, side and switch count to the farmhand. Both peers must observe the same entrance, worker, target sequence, actions, and return, without cycling through crop edges at the stalled entrance.
5. Verify every reachable dry crop is watered before completion.
6. Verify only the requesting farmhand's money changes, exactly once, by the reported settlement; host money is unchanged.
7. Verify both peers receive the same final work count and contract ID disappears from `efo_netstatus`.

### 2. Farmhand requests harvest and delivery

1. Record both inventories, all eligible chest contents, overflow contents, money, mature crop state, and network status.
2. On the farmhand, request one harvest contract.
3. Verify both peers observe the same NPC, deterministic mature-target sequence, harvest actions, delivery paths, and return.
4. Verify every reachable mature crop is handled before completion. A single-harvest crop is removed and a regrowing crop enters its regrowth state.
5. Verify the NPC routes around trellises, kegs, chests, machines, and fences; no placed object is removed, moved, replaced, or loses state.
6. Verify every exact output, quality, stack, and by-product appears once in the ranked chest route. When an eligible chest accepts the item, both player inventories and the shipping bin must remain unchanged.
7. Make all eligible chests unable to accept one output while the requesting farmhand remains on the main farm with inventory capacity. Verify that exact output enters only the requesting farmhand's inventory once; the host inventory and shipping bin remain unchanged.
8. Repeat with the requester off the farm or without inventory capacity. Verify the exact remainder appears once in `efo_overflow`; if emergency dropping is forced, verify an explicit warning and visible exact drop at the on-farm requester, otherwise at a collision-free farmhouse/selected-entrance delivery tile. It must not appear inside a blocked worker tile. Then force both overflow-lock failure and a ground-drop exception: verify the exact remainder appears once in `efo_quarantine`, carries one transfer ID, survives save/reload, and is reported in the quarantine destination count. If the quarantine write is also temporarily withheld, verify the host retains a recovery record, rejects new harvest contracts, and restores the exact stack once before clearing the record. Repeat at day end, ordinary save, and initial save creation with the recovery-record path withheld; verify the exact transient remainder is forced into `efo_quarantine` before the save completes and the NPC lease and wage settle once.
9. Verify only the requesting farmhand pays the reported wage once.

### 3. Host request and simultaneous request ordering

1. Have host and farmhand open contract previews before either confirms.
2. Confirm on both peers as close together as practical.
3. Verify the host accepts exactly one request in receipt order and explicitly rejects the other as already active.
4. Verify only the accepted requester is charged and exactly one NPC lease, target mutation, and settlement occur.
5. After completion, have the host request the other task and verify the farmhand observes matching snapshots and final result.

### 4. Disconnect, reconnect, and replay safety

1. Start a farmhand harvest request and disconnect the farmhand after acceptance but before delivery finishes.
2. Verify the host continues safe delivery/overflow and restores the NPC; no cargo is lost or duplicated.
3. Reconnect the same farmhand. Verify the active snapshot or processed final result is restored and the requesting farmer receives the correct refund despite the disconnect.
4. Repeat with a disconnect immediately after confirmation/pending feedback. Delay an old-session response and sync state until after the host reconnects. Verify the farmhand accepts only the sync state whose nonce matches its latest sync request, establishes that response's new host session, ignores both delayed old-session messages, then resends the pending request once with its original request ID. The host must return the prior request result and must not dispatch or charge twice.

### 5. Host save and restart replay safety

1. Complete a farmhand watering or harvest contract, record its request/contract result, then let the host save normally.
2. Quit and restart the host process, reconnect the same farmhand, and verify `efo_netstatus` shows a new host session with `recoveryHealthy=True` and a nonzero processed count.
3. Resend the exact saved request ID from the farmhand test harness. Verify the host returns the prior accepted response/result rebound to the new session, with no new lease, crop mutation, item transfer, charge, or refund.
4. Repeat with a previously rejected request and verify it remains rejected without reevaluating into a new contract.
5. In a disposable copy, corrupt or schema-mismatch the persisted recovery record. Include a duplicate transfer ID and a produced-item total which does not equal requester inventory plus chest, overflow, and visible-drop totals. Verify the host reports `recoveryHealthy=False` and rejects all new contracts without mutating money or world state.
6. In disposable copies created by the protocol-3 and protocol-4 development builds, retain a clean recovery ledger and load it with protocol 5. Verify the host validates and rebinds the prior response/result to the new session, reports `recoveryHealthy=True`, and an exact request replay causes no second contract, mutation, transfer, charge, or refund. A mixed or older-than-3 schema must still fail closed.

### 6. Rejection safety

For each case below, verify the request is rejected with no money, NPC, crop, cargo, chest, or overflow mutation:

- insufficient requester funds;
- requester not on the main farm;
- NPC becomes unavailable after preview;
- no valid dry/mature target;
- second request while a contract is active;
- mismatched Evil Farm Owner versions on the two peers.

## Final evidence

The multiplayer gate passes only when:

- both the watering and harvest scenarios pass over a real host/farmhand connection;
- both SMAPI logs contain no Evil Farm Owner error or unhandled exception;
- before/after money, crop, chest, overflow, and inventory counts reconcile exactly;
- disconnect/reconnect produces one contract, one mutation, and one settlement;
- host save/restart plus exact request replay produces no second contract, mutation, transfer, charge, or refund;
- the tested DLL SHA-256 and mod version are recorded in the Issue #33 or PR comment.
