#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package_path="$project_root/bin/Release/net6.0/EvilFarmOwner 0.1.0.zip"

cd "$project_root"

source_commit="$(git rev-parse HEAD)"
source_tree="$(git rev-parse 'HEAD^{tree}')"
source_status="$(git status --porcelain=v1 --untracked-files=all)"
if [[ -n "$source_status" && "${EFO_RELEASE_ALLOW_DIRTY:-0}" != "1" ]]; then
  echo "Release verification requires a clean Git worktree." >&2
  echo "Commit or remove these source changes before producing audited hashes:" >&2
  printf '%s\n' "$source_status" >&2
  echo "For a non-audited pre-commit diagnostic only, set EFO_RELEASE_ALLOW_DIRTY=1." >&2
  exit 1
fi

if [[ -n "$source_status" ]]; then
  echo "WARNING: running a non-audited verification from a dirty worktree." >&2
fi

echo "Source commit: $source_commit"
echo "Source tree: $source_tree"

dotnet run \
  -c Release \
  --project tests/EvilFarmOwner.LogicTests.csproj \
  -p:EnableModDeploy=false \
  -p:EnableModZip=false

dotnet build EvilFarmOwner.csproj \
  -c Release \
  -p:EnableModDeploy=false \
  -p:EnableModZip=true

if [[ ! -f "$package_path" ]]; then
  echo "Release package was not generated: $package_path" >&2
  exit 1
fi

expected_entries=$'EvilFarmOwner/EvilFarmOwner.dll\nEvilFarmOwner/assets/banner-v4-lowres.png\nEvilFarmOwner/assets/icon-v2.png\nEvilFarmOwner/i18n/default.json\nEvilFarmOwner/i18n/zh.json\nEvilFarmOwner/manifest.json'
actual_entries="$(unzip -Z1 "$package_path" | LC_ALL=C sort)"
if [[ "$actual_entries" != "$expected_entries" ]]; then
  echo "Release package contents differ from the allowlist:" >&2
  diff -u <(printf '%s\n' "$expected_entries") <(printf '%s\n' "$actual_entries") || true
  exit 1
fi

unzip -p "$package_path" EvilFarmOwner/manifest.json \
  | jq -e '
      .Name == "Evil Farm Owner"
      and .Author == "Aveouter"
      and .Version == "0.1.0"
      and .UniqueID == "Aveouter.EvilFarmOwner"
      and .EntryDll == "EvilFarmOwner.dll"
      and (.UpdateKeys | index("GitHub:Aveouter/EvilFarmOwner") != null)
    ' >/dev/null

if strings "$project_root/bin/Release/net6.0/EvilFarmOwner.dll" \
  | rg -q 'efo_work|efo_toggle|efo_status|WorkRadius|DailyWage|ClearDebris|PlantSeedsFromInventory|FertilizeEmptyDirt'; then
  echo "Release DLL still exposes a legacy prototype command or setting." >&2
  exit 1
fi

if command -v sha256sum >/dev/null 2>&1; then
  sha256sum "$package_path"
else
  shasum -a 256 "$package_path"
fi

echo "Verified source commit: $source_commit"
echo "Verified source tree: $source_tree"
echo "Release package verification passed."
