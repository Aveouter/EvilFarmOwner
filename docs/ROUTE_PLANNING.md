# Farm Work Route Planning

Named watering and harvesting contracts prefer the right/east main-road (Bus Stop) entrance, then fall back to other genuine farm-boundary entrances when the preferred side cannot provide a safe round trip. They do not reuse the NPC's vanilla schedule path because vanilla NPC movement may clear player-placed objects when blocked.

The arrival planner reads genuine map-boundary warps, clamps each off-map source tile to a visible edge anchor, and orders them right/east, bottom/south, top/north, then left/west. It performs at most eight safe path checks per side, so a blocked preferred entrance cannot consume the entire search budget. Farmhouse, greenhouse, cave, and other interior transfers are never worker entrances. Dispatch is committed only after the worker is present at the selected farm-edge tile and an object-safe route to a real target and back to that same entrance has been found.

Static collision and vanilla controller execution are separate safety boundaries. If a worker makes no pixel progress while still on the selected arrival tile before completing any work, the contract marks the whole entrance side failed instead of consuming crop interaction edges. It then replans with that side excluded, visibly relocates the same leased worker to the next genuine boundary entrance, and keeps the original reservation and contract ID. The authoritative multiplayer snapshot carries the selected arrival tile, side, and switch count. If every boundary entrance fails, the contract stops, restores the worker, and reports the entrance failure.

## Safety invariants

- Kegs, chests, machines, fence segments, raised-seed trellises, blocking terrain features, and other occupied tiles are obstacles. Ordinary non-trellis crop tiles follow vanilla passability. A gate is the only placed-object exception: it may be opened for passage, but it is never removed.
- The work lease disables `willDestroyObjectsUnderfoot`, charging, and accumulated blocked movement for the full contract, then restores the original values.
- Every attached path controller must use `nonDestructivePathing`; the lease rejects any controller which does not.
- Contract travel completes as soon as the worker enters the planned interaction tile, then aligns the worker to that tile's canonical pixel position before the next route starts. This avoids both the vanilla controller's final centering pin and a half-entered tile blocking the following route.
- The dispatch HUD identifies the actual selected entrance so a fallback arrival is explicit instead of looking like a missing worker.
- A target tile and its interaction tile are separate. A trellis crop can be acted on from a reachable cardinal neighbor without treating the crop tile as walkable.
- Interaction tiles are accepted from the live collision route, not `CanSpawnCharacterHere`. The latter rejects ordinary occupied HoeDirt even though Stardew marks non-trellis crops as passable, which previously made the interior of dense fields incorrectly unreachable. Raised-seed trellises remain blocked by `HoeDirt.isPassable` for ordinary NPCs.
- A route which becomes blocked is recorded as a failed target/interaction edge. The planner may try another side of the same crop, but it cannot retry the same failed edge indefinitely.

## Vanilla controller boundary

Stardew's schedule system parses a daily schedule into destinations, builds a fixed tile stack with `PathFindController`, and follows that stack through map warps. It does not continuously optimize around new obstacles. In its destructive NPC-schedule mode, collision checks may ignore a placed object because `willDestroyObjectsUnderfoot` allows the movement layer to clear it. In `nonDestructivePathing` mode, the controller instead opens a gate before entering it and stops when the next object is not passable.

Evil Farm Owner keeps the useful execution behavior but supplies its own live collision route. The current tile is removed from every precomputed controller path because a freshly warped NPC is already standing there; asking the vanilla controller to recenter inside that tile can collide with an adjacent building before the first real step. A pixel-progress watchdog replans after 180 unpaused update ticks and return travel gets three bounded replans before safe restoration.

If another activity replaces the leased NPC's controller, the contract enters a bounded recovery phase instead of discarding its lease state. It retries full restoration for 300 update ticks. When the other controller releases, the NPC is restored to the exact pre-contract location and movement state. If the conflict persists until that bound, saving, day end, or world closure, the contract removes only its own controller/lease marker and restores only the safety flags it changed; it never halts, warps, or replaces the controller owned by the other activity. Wage settlement and contract release occur once after either outcome.

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

Harvest delivery uses the same route map. Storage semantics still rank an exact compatible stack, the same item, the same category, and then available capacity before using actual walking distance as a tie-breaker. Chest selection no longer depends on the return entrance being reachable at that exact moment: cargo is delivered first, while target selection and return retain their own safety checks. If no reachable eligible chest can accept the item, the item goes directly to the contract requester only when that exact online farmer is still on the main farm and their inventory can accept it. The remaining fallback order is persistent team overflow and then an explicit visible ground drop. Emergency ground placement never defaults immediately to a possibly trapped worker: it uses the on-farm requester first, otherwise searches deterministic collision-free tiles around the farmhouse delivery area and selected entrance before using the worker position as the final last resort. Cargo is delivered after each harvested target, every transfer logs its item, quality, count, destination, and remainder, and settlement audits harvested count against requester inventory, chest, overflow, visible drop, and unresolved totals.

For vanilla crop mutation, `Crop.harvest` writes every ordinary, extra-yield, and special by-product item through the contract-owned `JunimoHarvester` collector. Its Boolean return is used only as the vanilla request to remove a single-harvest crop; it is not treated as harvest success, because regrowing crops successfully emit items and then return `false`. The non-empty captured item set is the source of truth for successful contract output.

## Reference boundary

The controller behavior above was verified against the installed 1.6 API surface and the pinned [Stardew Valley 1.6 `PathFindController` decompilation](https://github.com/AcidicNic/StardewValleyDecompiled1.6/blob/2878fb248092f9f5b8704ad2cc7e19d0abe1cf45/StardewValley.Pathfinding/PathFindController.cs). This is used to understand runtime contracts such as waypoint stacks, gate opening, warps, and non-destructive termination; no decompiled source is copied into the mod.

The dynamic loop is inspired by the public target-set behavior in [ActionQueue Reborn](https://github.com/myxal/ActionQueue-Reborn) and [DST Mod Autopilot](https://github.com/AlexXsWx/DST_mod_Autopilot): retain candidates, choose a nearby valid action, revalidate, remove the completed or invalid candidate, and repeat. Evil Farm Owner reimplements the idea against Stardew Valley's tile collision and contract safety rules; no source code is copied from those projects.
