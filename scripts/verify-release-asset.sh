#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 3 ]]; then
  echo "Usage: $0 <downloaded-zip> <expected-sha256> <stable-version>" >&2
  exit 2
fi

asset_path="$1"
expected_sha256="$(printf '%s' "$2" | LC_ALL=C tr 'A-F' 'a-f')"
expected_version="$3"

if [[ ! -f "$asset_path" ]]; then
  echo "Downloaded release asset does not exist: $asset_path" >&2
  exit 1
fi
if [[ ! "$expected_sha256" =~ ^[0-9a-f]{64}$ ]]; then
  echo "Expected SHA-256 must contain exactly 64 hexadecimal characters." >&2
  exit 2
fi
if [[ ! "$expected_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Release asset audit requires a stable semantic version without a prerelease suffix." >&2
  exit 2
fi

expected_name="EvilFarmOwner $expected_version.zip"
if [[ "$(basename "$asset_path")" != "$expected_name" ]]; then
  echo "Release asset name must be '$expected_name'." >&2
  exit 1
fi

if command -v sha256sum >/dev/null 2>&1; then
  actual_sha256="$(sha256sum "$asset_path" | awk '{print $1}')"
else
  actual_sha256="$(shasum -a 256 "$asset_path" | awk '{print $1}')"
fi
if [[ "$actual_sha256" != "$expected_sha256" ]]; then
  echo "Downloaded asset SHA-256 mismatch." >&2
  echo "Expected: $expected_sha256" >&2
  echo "Actual:   $actual_sha256" >&2
  exit 1
fi

expected_entries=$'EvilFarmOwner/EvilFarmOwner.dll\nEvilFarmOwner/LICENSE\nEvilFarmOwner/assets/banner-v4-lowres.png\nEvilFarmOwner/assets/icon-v2.png\nEvilFarmOwner/i18n/default.json\nEvilFarmOwner/i18n/zh.json\nEvilFarmOwner/manifest.json'
actual_entries="$(unzip -Z1 "$asset_path" | LC_ALL=C sort)"
if [[ "$actual_entries" != "$expected_entries" ]]; then
  echo "Downloaded release contents differ from the package allowlist:" >&2
  diff -u <(printf '%s\n' "$expected_entries") <(printf '%s\n' "$actual_entries") || true
  exit 1
fi

unzip -p "$asset_path" EvilFarmOwner/manifest.json \
  | jq -e --arg version "$expected_version" '
      .Name == "Evil Farm Owner"
      and .Author == "Aveouter"
      and .Version == $version
      and .UniqueID == "Aveouter.EvilFarmOwner"
      and .EntryDll == "EvilFarmOwner.dll"
      and (.UpdateKeys | index("GitHub:Aveouter/EvilFarmOwner") != null)
    ' >/dev/null

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if ! cmp -s "$project_root/LICENSE" <(unzip -p "$asset_path" EvilFarmOwner/LICENSE); then
  echo "Downloaded LICENSE differs from the reviewed repository license." >&2
  exit 1
fi

dll_search_text="$(
  unzip -p "$asset_path" EvilFarmOwner/EvilFarmOwner.dll \
    | LC_ALL=C tr -d '\000'
)"
if [[ "$dll_search_text" =~ efo_work|efo_toggle|efo_status|efo_acceptance_faults|WorkRadius|DailyWage|ClearDebris|PlantSeedsFromInventory|FertilizeEmptyDirt ]]; then
  echo "Downloaded DLL exposes a legacy prototype or acceptance-test command/setting." >&2
  exit 1
fi

echo "Verified downloaded asset: $asset_path"
echo "Verified stable version: $expected_version"
echo "Verified SHA-256: $actual_sha256"
echo "Downloaded release asset verification passed."
