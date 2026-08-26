# Single-worker Route Acceptance

Run this gate in a disposable single-player save with the exact production candidate. Do not use an acceptance-fault DLL for this matrix. Record the Mod DLL SHA-256, game/SMAPI versions, save date, worker, result, final HUD reason, and relevant Debug/Warn log lines for every row.

## Invariants for every scenario

- The worker uses a genuine farm-boundary entrance, never the farmhouse or cave.
- Chests, machines, fences, gates, decorations, crops, and terrain are not destroyed or displaced.
- Opening a gate is allowed; leaving it in an invalid state is not.
- One hire produces one reservation and one final settlement. A retry never charges again.
- Every captured or detached item reconciles exactly to requester inventory, a classified chest, overflow, visible drop, quarantine, or an unresolved recovery record.
- Each retry writes a Debug interruption snapshot. The final exhausted route writes Warn and a concise localized HUD reason.

## Route matrix

| Scenario | Setup and action | Pass condition |
| --- | --- | --- |
| Farm obstacle | Put a movable obstacle on the active route after dispatch. | The worker excludes the failed tile or directed edge, replans within three attempts, and preserves the obstacle. |
| Narrow entrance | Fence a one-tile corridor with a closed gate on the viable approach. | The NPC opens the gate and passes without destructive pathing or an entrance loop. |
| Dense and trellis crops | Mix ordinary crops with hops or grapes and leave multiple dry and mature targets behind them. | All reachable targets use cardinal interaction edges; only truly isolated targets are skipped. |
| Harvest delivery | Provide exact-stack, same-item, same-category, and empty fallback chests, then obstruct the selected delivery path. | Classification stays deterministic, cargo reaches the selected compatible chest after replanning, or enters lossless recovery after exhaustion. |
| Animal-house door | Obstruct barn/coop approach, indoor animal routes, and the return to the human door one at a time. | A failed animal/building is isolated; an exhausted exit safely stops and restores the worker. |
| Chest sorting | Obstruct source, destination, and return paths in separate runs. | Pre-transfer failure leaves the source untouched; post-detach failure rolls back or persists recovery before return. |
| Player changes map | Leave the farm while each stage is moving and acting. | Host execution continues on the farm and settles once without depending on the requester camera or location. |
| Dynamic return obstacle | Block the route to the arrival tile after the last action. | The worker replans up to three times; final failure restores safely and reports the actual interruption class. |

## Evidence summary

The matrix is complete only when every row has recorded evidence and both checks below pass:

1. Before/after world and item counts reconcile with no placed-object mutation.
2. The SMAPI log contains no Evil Farm Owner error or unhandled exception.

Real host/farmhand two-process acceptance is intentionally separate and remains a v0.5.0 gate in `MULTIPLAYER_TEST_PLAN.md`.
