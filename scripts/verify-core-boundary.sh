#!/usr/bin/env bash
set -euo pipefail

status=0
if grep -RInE 'Stardew|Microsoft\.Xna|SMAPI|Netcode' src/Core --include='*.cs'; then
    printf 'Core source must not reference game or mod-loader assemblies.\n' >&2
    status=1
fi

while IFS= read -r file; do
    if ! grep -Eq 'Stardew|Microsoft\.Xna|SMAPI|Netcode' "$file"; then
        printf 'Pure logic source must live under src/Core: %s\n' "$file" >&2
        status=1
    fi
done < <(find src -maxdepth 1 -type f -name '*.cs' -print | LC_ALL=C sort)

if ! grep -Fq '<ProjectReference Include="../EvilFarmOwner.Core.csproj" />' \
    tests/EvilFarmOwner.LogicTests.csproj; then
    printf 'Logic tests must reference EvilFarmOwner.Core.csproj directly.\n' >&2
    status=1
fi

exit "$status"
