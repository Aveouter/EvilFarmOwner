# Harvest Storage Fault Acceptance

This procedure is the live release gate for Issue #42. Use only a disposable save.
The acceptance command is excluded from ordinary Release builds and must never be
included in a distributed DLL.

## Build the temporary acceptance DLL

Quit Stardew Valley, then build and deploy the explicitly instrumented DLL:

```bash
dotnet build EvilFarmOwner.csproj -t:Rebuild \
  -c Release \
  -p:EnableAcceptanceFaults=true \
  -p:EnableModDeploy=true \
  -p:EnableModZip=false
```

SMAPI must log `ACCEPTANCE TEST BUILD` at startup. The command
`efo_acceptance_faults status` must report `none` before each scenario.

## Scenario A: persistent recovery record

1. Prepare one mature crop. Keep at least one ordinary eligible main-farm chest
   so the harvest dispatch preflight can pass, but fill every eligible chest so
   none can accept the harvested stack. Completely fill the requesting player's
   inventory too.
2. Run:

   ```text
   efo_acceptance_faults arm overflow-lock visible-drop quarantine-lock
   ```

3. Hire an NPC to harvest. After the item is captured, run
   `efo_acceptance_faults finalize`.
4. Verify the contract stops once, the result reports the exact stack under
   quarantine, `efo_netstatus` reports `quarantineHealthy=False`, and a new
   harvest request is rejected.
5. Save and reload. Run `efo_acceptance_faults clear`, then `efo_quarantine`.
   Verify the exact item, quality, stack and modData appear once. Close and
   reopen `efo_quarantine`; no duplicate may appear. `efo_netstatus` must now
   report `quarantineHealthy=True`.

## Scenario B: save-boundary forced quarantine

1. Prepare the same full-chest/full-inventory setup and run:

   ```text
   efo_acceptance_faults arm overflow-lock visible-drop quarantine-lock recovery-record-write
   ```

2. Hire an NPC to harvest. After capture, run
   `efo_acceptance_faults finalize`.
3. Verify the ordinary recovery-record write fails explicitly, then the
   save-boundary fallback places the original Item instance in quarantine and
   settles the contract once.
4. Clear faults, save, reload, and open `efo_quarantine`. Verify the exact item
   survives once and the SMAPI log contains no unhandled exception.

## Scenario C: fail-closed terminal write

Arm all five faults, capture one output, then invoke `finalize`. Verify the log
contains a CRITICAL save-boundary failure, the active contract remains reported,
no successful result is published, and no second contract can start. This is a
negative safety observation, not a releasable success path; clear the faults and
invoke `finalize` again before saving so the exact cargo enters quarantine once.

## Restore the production candidate

Quit the game, clear all faults, then rebuild without the acceptance property:

```bash
dotnet build EvilFarmOwner.csproj -t:Rebuild \
  -c Release \
  -p:EnableModDeploy=true \
  -p:EnableModZip=false
./scripts/verify-release.sh
```

The clean verifier forces a non-incremental production rebuild with
`EnableAcceptanceFaults=false`; it then scans both ordinary metadata and UTF-16
.NET user strings and must not find `efo_acceptance_faults`. Record the tested
production DLL hash in Issue #42 and PR #43.
