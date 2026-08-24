# Farm Work Route Planning

Named watering and harvesting contracts dispatch the leased NPC at a genuine external farm entrance. They do not reuse the NPC's vanilla schedule path because vanilla NPC movement may clear player-placed objects when blocked.

The arrival planner reads the farm map's boundary warps, clamps their off-map source tiles to visible edge anchors, and interleaves a bounded search around every entrance. Interior transfer tiles such as farmhouse, greenhouse, cave, and custom-map doors are excluded because their source tiles are not on the map boundary. Dispatch is committed only after the worker is present at the planned farm-edge tile and an object-safe route to a real target has been found.

## Safety invariants

- Kegs, chests, machines, fence segments, crops, terrain features, and other occupied tiles are obstacles. A gate is the only placed-object exception: it may be opened for passage, but it is never removed.
- The work lease disables `willDestroyObjectsUnderfoot`, charging, and accumulated blocked movement for the full contract, then restores the original values.
- Every attached path controller must use `nonDestructivePathing`; the lease rejects any controller which does not.
- A target tile and its interaction tile are separate. A trellis crop can be acted on from a reachable cardinal neighbor without treating the crop tile as walkable.
- A route which becomes blocked is recorded as a failed target/interaction edge. The planner may try another side of the same crop, but it cannot retry the same failed edge indefinitely.

## Vanilla controller boundary

Stardew's schedule system parses a daily schedule into destinations, builds a fixed tile stack with `PathFindController`, and follows that stack through map warps. It does not continuously optimize around new obstacles. In its destructive NPC-schedule mode, collision checks may ignore a placed object because `willDestroyObjectsUnderfoot` allows the movement layer to clear it. In `nonDestructivePathing` mode, the controller instead opens a gate before entering it and stops when the next object is not passable.

Evil Farm Owner keeps the useful execution behavior but supplies its own live collision route. The current tile is removed from every precomputed controller path because a freshly warped NPC is already standing there; asking the vanilla controller to recenter inside that tile can collide with an adjacent building before the first real step. A pixel-progress watchdog replans after 180 unpaused update ticks and return travel gets three bounded replans before safe restoration.

All current contract controllers execute on the farm, so they always use the non-destructive policy. A future cross-map travel phase may use a separate outside-farm escalation policy (open doors first, replan, then allow vanilla destructive clearing only after a confirmed stall), but that policy must never remain enabled after entering a farm.

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

The controller behavior above was verified against the installed 1.6 API surface and the pinned [Stardew Valley 1.6 `PathFindController` decompilation](https://github.com/AcidicNic/StardewValleyDecompiled1.6/blob/2878fb248092f9f5b8704ad2cc7e19d0abe1cf45/StardewValley.Pathfinding/PathFindController.cs). This is used to understand runtime contracts such as waypoint stacks, gate opening, warps, and non-destructive termination; no decompiled source is copied into the mod.

The dynamic loop is inspired by the public target-set behavior in [ActionQueue Reborn](https://github.com/myxal/ActionQueue-Reborn) and [DST Mod Autopilot](https://github.com/AlexXsWx/DST_mod_Autopilot): retain candidates, choose a nearby valid action, revalidate, remove the completed or invalid candidate, and repeat. Evil Farm Owner reimplements the idea against Stardew Valley's tile collision and contract safety rules; no source code is copied from those projects.
