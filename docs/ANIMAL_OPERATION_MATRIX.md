# Vanilla animal-operation matrix

Outdoor main-farm petting now participates in complete shifts with stable animal IDs, host-owned mutation, non-destructive routes, and final reconciliation. Barn/coop interiors and the remaining rows stay disabled until each has a location-aware route, commit check, and recovery path. This matrix records the vanilla state that must be preserved.

| Operation | Eligibility | Required state transition | Product/storage rule |
| --- | --- | --- | --- |
| Pet | Host, awake animal, `wasPet == false` | Use the normal manual-pet effects once, including the reduced gain after auto-petting, mood/friendship caps, profession effects, farming XP, texture/emote state, and `wasPet` | No item output. A repeated or replayed target is skipped. |
| Fill trough | Empty map tile marked `Trough`; owned silo hay is available | Consume exactly one owned hay before adding exactly one hay object; stop when either capacity or hay reaches zero | Never create hay, take hay from player inventory implicitly, replace an occupied tile, or exceed valid trough capacity. |
| Loose overnight product | Object is in an animal-house location and its qualified ID is allowlisted by a resident animal's drop-over-night produce data | Remove one stable location/tile/object target only after cargo recovery is established | Route the exact item, stack, and quality through the shift destination. Never collect placed machines, furniture, quest items, or auto-grabber contents. |
| Milk or shear | Host; adult; non-null `currentProduce`; harvest type is `HarvestWithTool`; animal data names a tool; auto-grabber does not own the product | Create the configured product with stored quality and cracker stack, then preserve vanilla stats, clear `currentProduce`, add 5 friendship, reload texture, and grant 5 farming XP | The tool name is semantic input; the player does not need to own or equip a pail/shears. Clear produce only after the exact output has entered the recovery pipeline. |
| Dig-up product | Produced as a loose outdoor object by the animal | No direct animal mutation during collection | Later handled by a narrowly allowlisted loose-product collector; ordinary forage and player-placed objects remain excluded. |
| Auto-grabber | Machine owns its internal chest and collection timing | No mutation by the animal-care stage | Excluded. Sorting auto-grabber contents requires a separate locked-container design. |

## Location and door boundary

- A target key includes operation, building/location identity, animal ID or tile/object identity, and day.
- Animals are handled in their actual current location; an outdoor animal is not teleported home for convenience.
- Entering barns/coops requires a real building transition and an independently planned return path. Ordinary doors may be opened when allowed, but no farm object is destroyed.
- Sleeping, pregnancy/birth, festivals/events, unavailable interiors, and location changes cause an explicit skip or replan, never a blind mutation.
- Final reconciliation may discover an unclaimed target once more. A committed animal/day or product identity is never repeated.
