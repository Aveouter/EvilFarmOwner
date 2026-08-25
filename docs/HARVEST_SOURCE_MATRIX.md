# Harvest Source Matrix

This matrix prevents the complete shift from treating every object with a held item as the same kind of harvest source. Each source is enabled only after its vanilla collection state, ownership, follow-up triggers, and replay behavior are represented explicitly.

## Enabled

| Source | Vanilla state transition preserved | Item handling |
| --- | --- | --- |
| Mature crops | `Crop.harvest` and crop removal/regrowth result | Exact captured output and by-products enter the lossless destination pipeline. |
| Normal and heavy tree tappers | Clear ready output, then call the tree's tapper rescheduler | Exact output enters the lossless destination pipeline; rollback restores the ready item if rescheduling fails. |
| Fruit trees | Clear the tree's fruit list once after capturing every non-null fruit slot | Stored item, stack, and quality are preserved. Lightning-struck trees produce one coal per occupied fruit slot like vanilla. |
| Simple ready vanilla machines | Clear the held output and ready/display state, reset the sprite, apply declared harvest stats and experience, and never auto-load another input | Limited to exact vanilla `Object` machines with numeric IDs and no collect-time recalculation or `OutputCollected` continuation rule. Exact output metadata enters the existing lossless destination pipeline. |
| Crab pots | Preserve the deterministic Crabbing Book double-catch rule, caught-fish record/length, five fishing XP, consumed bait, lid/readiness state, and short removal guard | Collect the existing exact output through the lossless destination pipeline. Never refill bait automatically. |
| Fish ponds | Clear only the ready building-owned output and grant vanilla fishing experience: 10 plus 4% of the object's store value | Route to the pond's item-bucket edge, preserve the exact output, and never add fish or satisfy pond requests. |

## Requires a dedicated implementation

| Source | Why generic `heldObject` removal is unsafe |
| --- | --- |
| Stateful data-driven machines | Machines with collect-time recalculation or `OutputCollected` continuation rules can replace output, consume retained input, or immediately start another cycle. They remain excluded until that continuation can be rolled back exactly. |
| Berry and tea bushes | Output count, quality, experience, mutex behavior, and special walnut bushes depend on bush type and the requesting farmer. Special/map bushes must be excluded explicitly. |
| Farm-building interiors | Barns, coops, sheds, caves, and greenhouse-like locations need location-aware entrances, doors, route ownership, and return behavior before their contents can join a main-farm shift. |
| Loose forage and spawned objects | Ownership, quest/special items, spawned-object flags, and terrain coverage need an allowlist. Debris clearing is not harvest collection. |

## Excluded by policy

- Shipping-bin collection or automatic sale.
- Automatic machine refill or consumption of player inputs.
- Auto-grabber contents; those belong to the animal-care/storage design and require the chest mutex.
- Incubators, hatching devices, quest rewards, choice menus, buffs, arcade/furniture interactions, and other non-item interactions.
- Modded machines without an explicit compatibility contract.

## Simple-machine boundary

The generic ready-machine collector deliberately accepts only the subset whose vanilla collection is a finite clear-and-report transition. It excludes subclasses, non-numeric/modded IDs, incubators, tappers, chest-backed machines, collect-time recalculation, and any output-collected continuation rule. This keeps crystalariums and other retained-input machines from being cleared without correctly restarting their cycle. Automatic input loading is never called.
