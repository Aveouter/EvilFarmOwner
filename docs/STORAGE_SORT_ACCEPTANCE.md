# Deterministic Chest-Sorting Acceptance

Use only the isolated disposable save copy. The fixture command is excluded
from ordinary production builds.

1. Quit every running Stardew Valley process. Build with
   `-p:EnableStorageSortAcceptance=true`, deploy only to the isolated Mods
   profile, and load a main farm with no ordinary eligible farm chests.
2. Stand in an open part of the main farm and run
   `efo_storage_sort_fixture setup`. The command must report exactly five
   tagged chests and four preflighted transfers.
3. Hire an available adult NPC for one manual chest-sorting contract. Verify
   the four complete stacks exercise exact-stack, same-item/different-quality,
   same-category, and empty-chest routing without touching player inventory or
   the shipping bin.
4. Compare the completion report with the four source/destination chest tiles,
   item IDs, categories, qualities and quantities. No unrelated stack may
   change and the exact total item count must be conserved.
5. Run `efo_storage_sort_fixture status`. It must report `converged` and no
   remaining transfer. A second contract request must reject before reserving
   wages or leasing the NPC.
6. Retain the SMAPI log, then discard the isolated save copy. Never distribute
   the fixture-enabled DLL.
