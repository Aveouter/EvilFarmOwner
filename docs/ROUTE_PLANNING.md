# Farm Work Route Planning

Named watering and harvesting contracts dispatch the leased NPC at the farm's left entrance. They do not reuse the NPC's vanilla schedule path because vanilla NPC movement may clear player-placed objects when blocked.

## Safety invariants

- Kegs, chests, machines, fences, crops, terrain features, and other occupied tiles are obstacles. Contract movement never removes or moves them.
- The work lease disables `willDestroyObjectsUnderfoot`, charging, and accumulated blocked movement for the full contract, then restores the original values.
- Every attached path controller must use `nonDestructivePathing`; the lease rejects any controller which does not.
- A target tile and its interaction tile are separate. A trellis crop can be acted on from a reachable cardinal neighbor without treating the crop tile as walkable.
- A route which becomes blocked is recorded as a failed target/interaction edge. The planner may try another side of the same crop, but it cannot retry the same failed edge indefinitely.

## Dynamic shortest-path algorithm

At dispatch and after every action, route interruption, or delivery, the host performs one breadth-first scan from the NPC's current tile over Stardew's current collision map. The scan produces the exact shortest walking distance and predecessor chain for every reachable tile in the same connected component.

The planner then:

1. enumerates every currently actionable crop;
2. enumerates its four cardinal interaction tiles;
3. removes occupied, unreachable, and previously failed edges;
4. selects the lowest actual path cost;
5. breaks ties deterministically by target row, target column, interaction row, and interaction column;
6. reconstructs only the selected path and revalidates the crop immediately before the action.

The scan is `O(V + E)` for the reachable farm grid. It replaces the previous approach of running a separate A* search for every crop side, which scaled poorly as crop count increased.

Harvest delivery uses the same route map. Storage semantics still rank an exact compatible stack, the same item, the same category, and then available capacity before using actual walking distance as a tie-breaker. Cargo is delivered after each harvested target so the NPC never silently loses an item between replans.

## Reference boundary

The dynamic loop is inspired by the public target-set behavior in [ActionQueue Reborn](https://github.com/myxal/ActionQueue-Reborn) and [DST Mod Autopilot](https://github.com/AlexXsWx/DST_mod_Autopilot): retain candidates, choose a nearby valid action, revalidate, remove the completed or invalid candidate, and repeat. Evil Farm Owner reimplements the idea against Stardew Valley's tile collision and contract safety rules; no source code is copied from those projects.
