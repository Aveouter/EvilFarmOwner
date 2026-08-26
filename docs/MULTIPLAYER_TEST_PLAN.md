# v0.5.0 one-pass multiplayer acceptance

This is the final live release gate for Issue #136 and draft PR #143. Run the matrix once against the exact production candidate. Split-screen is not evidence for the remote protocol rows.

Maintainer decision (2026-08-27): this matrix was explicitly waived for the initial merge and was not performed. Nothing in this document should be read as a passing result. Keep it as the future regression/validation procedure, and do not rerun individual rows repeatedly.

## Evidence package

Before starting, record:

- the candidate version and SHA-256 of `EvilFarmOwner.dll` on both peers;
- Stardew Valley and SMAPI versions;
- save ID, in-game date, host/farmhand IDs, starting money, and chest/inventory counts;
- both complete SMAPI logs and one screenshot of the final roster/contract UI.

Use two independent Stardew Valley processes with the identical DLL. Keep both consoles visible. Use a disposable co-op save with four currently available adult NPCs, at least eight mature crops, at least eight dry crops, trellises, two animal buildings, ready animal products, several sortable chests, closed gates, and placed machines that create narrow routes.

Run `efo_netstatus` on both peers before the first contract and after every reconnect or restart. Both peers must agree on the host session and active contract ID; the host must report `recoveryHealthy=True` and `quarantineHealthy=True`.

If an invariant fails, stop the matrix, preserve both logs, and file one focused issue. Do not repeatedly rerun the same scenario in the release session.

After the single run, generate the evidence skeleton and automated consistency checks with:

```bash
scripts/collect-multiplayer-evidence.sh \
  /path/to/host/EvilFarmOwner.dll \
  /path/to/farmhand/EvilFarmOwner.dll \
  /path/to/host/SMAPI-latest.txt \
  /path/to/farmhand/SMAPI-latest.txt \
  /path/to/evidence.md
```

The output file must not already exist. The script is read-only with respect to both game installations and logs. It checks identical peer DLL hashes, at least two network-status records per peer, final session/contract agreement, host recovery health, and Evil Farm Owner error lines. Complete every generated `TODO` and attach the two original logs; the script does not replace any manual matrix row.

## Matrix

### 1. One-worker baseline

1. Set `MaximumConcurrentWorkers` to 1 on the host and confirm a farmhand-requested shift.
2. Verify one worker performs the enabled stages in the released order, enters from a genuine farm boundary, opens gates, preserves placed objects, and continues while the requesting player leaves the farm.
3. Run `efo_report` on both peers. Work, items, one worker settlement, charge, refund, and contract ID must agree.

### 2. Four-worker deterministic work

1. Set the host limit to 4. Select four workers manually, record the displayed aggregate authorization, then confirm once.
2. Verify all four receive the same group contract ID and independent snapshots. Harvest and watering targets must be uniquely divided before travel; animal care and storage sorting must each have only one owner.
3. Verify workers do not occupy the same tile or traverse an edge in opposite directions. Shared farm/building entrances wait or replan; equal coordinates in different barns/coops must not block each other.
4. Verify a completed worker snapshot is retired without removing other workers. The final HUD is one group summary, and `efo_report` names all workers.
5. Verify total charge/refund equals the sum of the four worker settlements and the player's money changes exactly once by the net total.
6. On the next day, enable an automatic contract with the same authorization pool. Verify it selects up to four currently available adults in the documented efficiency, wage, friendship, and name order, skips unavailable NPCs, and starts only one daily group.

### 3. Standby and failed-stage reassignment

1. Prepare fewer initial crop targets than selected workers so at least one selected NPC has no unclaimed first target.
2. Confirm the idle NPC remains a zero-hour standby with no charge or refund instead of disappearing from the final roster.
3. After another worker starts a route, place a non-destructible obstacle that exhausts that worker's bounded target route.
4. Verify only the failed worker's uncommitted target is released. The standby receives one recovery assignment and one normal billable settlement; completed targets are not replayed.
5. Repeat only the accounting observation with no standby available: a successful paid worker may recover the failed stage without a second charge.

### 4. Route and storage contention

1. Make two crop routes share a closed one-tile gate and make two delivery routes prefer the same classified chest.
2. Verify intentional reservation waits do not emit false timeout/stall failures, the gate opens, no object is destroyed, and chest mutation occurs only while its mutex is held.
3. Fill the preferred chest between planning and delivery. Verify deterministic rerouting to another compatible chest or the existing lossless recovery chain; no item may enter the shipping bin or disappear.
4. Verify completed storage-transfer reports have one contiguous sequence with no duplicate sequence or transfer ID. A recovered successful group has no stale skipped report.

### 5. Departure, disconnect, and reconnect

1. Start a four-worker shift from the farmhand, then move the requester off the farm. Host work must continue; inventory delivery may switch to the configured chest path without losing cargo.
2. Disconnect the farmhand during active harvest delivery. The host must continue safely and retain exactly one result.
3. Reconnect the same farmhand. It must receive the current per-worker snapshots or the final result under the same contract, never a second dispatch or charge.
4. Delay an old-session response until after reconnect. The farmhand must ignore it, establish only the nonce-matched session, and resend a pending request with its original request ID.

### 6. Save boundary, day end, and host restart

1. Save during each of harvest delivery, animal-house travel, and chest sorting in disposable copies. The group must stop once, settle once, restore every lease, and persist or quarantine every detached item before the save completes.
2. Force day end with two to four workers active. Each worker must restore independently; one failure must not release another worker's lease, route, cargo, or settlement.
3. Complete a farmhand shift, save, restart the host, and reconnect. The host session must change while the processed request/result ledger remains healthy.
4. Replay the exact saved request ID. The host must return the prior response/result rebound to the new session with no new lease, mutation, transfer, charge, or refund.
5. Load a clean schema-10 recovery record. It must migrate as one worker. A corrupt, mixed, duplicate-transfer, or conservation-breaking record must fail closed and disable new contracts.

### 7. Rejection and configuration ownership

For each row, verify no money, NPC, target, cargo, chest, or recovery state changes:

- farmhand attempts to change the host-owned worker limit;
- request contains zero, duplicate, or more workers than the host limit;
- all selected NPCs become unavailable before confirmation;
- requester leaves the farm before confirmation;
- insufficient aggregate authorization;
- a second request arrives while a group is active;
- peer mod versions differ;
- every enabled stage has no supported work.

## Release decision

When the deferred live validation is eventually performed, it passes only when every matrix row has one recorded result and all of these invariants hold:

1. No Evil Farm Owner error or unhandled exception appears in either SMAPI log.
2. No placed object is destroyed and no NPC remains leased, hidden, duplicated, or schedule-stalled after settlement.
3. Every produced or detached item reconciles exactly to requester inventory, classified chest, overflow, visible drop, quarantine, or a single unresolved recovery record.
4. Every target mutation and request ID occurs at most once.
5. Aggregate work, item destinations, worker-hours, charge, and refund equal the per-worker totals.
6. The exact tested DLL hash and evidence summary are posted to Issue #136 and PR #143 before the PR is marked ready.
