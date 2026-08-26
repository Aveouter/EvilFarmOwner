#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$project_root"

manifest_version="$(jq -er '.Version | select(test("^[0-9]+\\.[0-9]+\\.[0-9]+(-[0-9A-Za-z.-]+)?$"))' manifest.json)"
project_version="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' EvilFarmOwner.csproj)"
if [[ "$project_version" != "$manifest_version" ]]; then
  echo "Project version '$project_version' does not match manifest version '$manifest_version'." >&2
  exit 1
fi
package_path="$project_root/bin/Release/net6.0/EvilFarmOwner $manifest_version.zip"

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

"$project_root/scripts/verify-core-boundary.sh"

dotnet build EvilFarmOwner.Core.csproj \
  -c Release

dotnet run \
  -c Release \
  --project tests/EvilFarmOwner.LogicTests.csproj \
  -p:EnableAcceptanceFaults=false \
  -p:EnableModDeploy=false \
  -p:EnableModZip=false

dotnet build EvilFarmOwner.csproj \
  -t:Rebuild \
  -c Release \
  -p:EnableAcceptanceFaults=false \
  -p:EnableModDeploy=false \
  -p:EnableModZip=true

if [[ ! -f "$package_path" ]]; then
  echo "Release package was not generated: $package_path" >&2
  exit 1
fi

expected_entries=$'EvilFarmOwner/EvilFarmOwner.dll\nEvilFarmOwner/LICENSE\nEvilFarmOwner/assets/banner-v4-lowres.png\nEvilFarmOwner/assets/icon-v2.png\nEvilFarmOwner/i18n/default.json\nEvilFarmOwner/i18n/zh.json\nEvilFarmOwner/manifest.json'
actual_entries="$(unzip -Z1 "$package_path" | LC_ALL=C sort)"
if [[ "$actual_entries" != "$expected_entries" ]]; then
  echo "Release package contents differ from the allowlist:" >&2
  diff -u <(printf '%s\n' "$expected_entries") <(printf '%s\n' "$actual_entries") || true
  exit 1
fi

unzip -p "$package_path" EvilFarmOwner/manifest.json \
  | jq -e --arg version "$manifest_version" '
      .Name == "Evil Farm Owner"
      and .Author == "Aveouter"
      and .Version == $version
      and .UniqueID == "Aveouter.EvilFarmOwner"
      and .EntryDll == "EvilFarmOwner.dll"
      and (.UpdateKeys | index("GitHub:Aveouter/EvilFarmOwner") != null)
    ' >/dev/null

if ! cmp -s LICENSE <(unzip -p "$package_path" EvilFarmOwner/LICENSE); then
  echo "Packaged LICENSE differs from the reviewed repository license." >&2
  exit 1
fi

dll_search_text="$(LC_ALL=C tr -d '\000' < "$project_root/bin/Release/net6.0/EvilFarmOwner.dll")"
if [[ "$dll_search_text" =~ efo_work|efo_toggle|efo_status|efo_acceptance_faults|WorkRadius|DailyWage|ClearDebris|PlantSeedsFromInventory|FertilizeEmptyDirt ]]; then
  echo "Release DLL still exposes a legacy prototype or acceptance-test command/setting." >&2
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
