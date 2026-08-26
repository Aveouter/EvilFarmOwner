#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 5 ]]; then
  echo "Usage: $0 <host-dll> <farmhand-dll> <host-smapi-log> <farmhand-smapi-log> <output-md>" >&2
  exit 2
fi

host_dll="$1"
farmhand_dll="$2"
host_log="$3"
farmhand_log="$4"
output_path="$5"

for input_path in "$host_dll" "$farmhand_dll" "$host_log" "$farmhand_log"; do
  if [[ ! -f "$input_path" || ! -s "$input_path" ]]; then
    echo "Acceptance evidence input is missing or empty: $input_path" >&2
    exit 1
  fi
done
if [[ -e "$output_path" ]]; then
  echo "Refusing to overwrite existing evidence summary: $output_path" >&2
  exit 1
fi
output_parent="$(dirname "$output_path")"
if [[ ! -d "$output_parent" ]]; then
  echo "Evidence output directory does not exist: $output_parent" >&2
  exit 1
fi

sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  else
    shasum -a 256 "$1" | awk '{print $1}'
  fi
}

last_network_status() {
  grep -E 'EFO network:' "$1" | tail -n 1
}

status_field() {
  local status_line="$1"
  local field_name="$2"
  printf '%s\n' "$status_line" \
    | sed -nE "s/.*${field_name}=([^,[:space:]]+).*/\\1/p"
}

count_network_statuses() {
  grep -Ec 'EFO network:' "$1" || true
}

find_efo_errors() {
  grep -Ein \
    'ERROR[[:space:]]+(Evil Farm Owner|Aveouter\.EvilFarmOwner)|unhandled exception' \
    "$1" || true
}

host_sha256="$(sha256_file "$host_dll")"
farmhand_sha256="$(sha256_file "$farmhand_dll")"
if [[ "$host_sha256" != "$farmhand_sha256" ]]; then
  echo "Host and farmhand EvilFarmOwner.dll hashes differ." >&2
  echo "Host:     $host_sha256" >&2
  echo "Farmhand: $farmhand_sha256" >&2
  exit 1
fi

host_status_count="$(count_network_statuses "$host_log")"
farmhand_status_count="$(count_network_statuses "$farmhand_log")"
if (( host_status_count < 2 || farmhand_status_count < 2 )); then
  echo "Each peer log must contain at least two efo_netstatus records." >&2
  echo "Host: $host_status_count; farmhand: $farmhand_status_count" >&2
  exit 1
fi

host_status="$(last_network_status "$host_log")"
farmhand_status="$(last_network_status "$farmhand_log")"
host_role="$(status_field "$host_status" role)"
farmhand_role="$(status_field "$farmhand_status" role)"
host_session="$(status_field "$host_status" session)"
farmhand_session="$(status_field "$farmhand_status" session)"
host_active="$(status_field "$host_status" active)"
farmhand_active="$(status_field "$farmhand_status" active)"

if [[ "$host_role" != "host" || "$farmhand_role" != "farmhand" ]]; then
  echo "The supplied logs do not end with the expected host/farmhand roles." >&2
  exit 1
fi
if [[ ! "$host_session" =~ ^[0-9a-fA-F]{32}$
      || "$host_session" != "$farmhand_session" ]]; then
  echo "Peers do not agree on a valid final host session." >&2
  echo "Host: $host_session; farmhand: $farmhand_session" >&2
  exit 1
fi
if [[ "$host_active" != "$farmhand_active" ]]; then
  echo "Peers do not agree on the final active contract." >&2
  echo "Host: $host_active; farmhand: $farmhand_active" >&2
  exit 1
fi
if [[ "$host_status" != *"recoveryHealthy=True"*
      || "$host_status" != *"quarantineHealthy=True"* ]]; then
  echo "The host final status is not recovery/quarantine healthy." >&2
  exit 1
fi

host_errors="$(find_efo_errors "$host_log")"
farmhand_errors="$(find_efo_errors "$farmhand_log")"
if [[ -n "$host_errors" || -n "$farmhand_errors" ]]; then
  echo "Evil Farm Owner error or unhandled-exception lines were found." >&2
  if [[ -n "$host_errors" ]]; then
    printf 'Host log:\n%s\n' "$host_errors" >&2
  fi
  if [[ -n "$farmhand_errors" ]]; then
    printf 'Farmhand log:\n%s\n' "$farmhand_errors" >&2
  fi
  exit 1
fi

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_commit="$(git -C "$project_root" rev-parse HEAD)"
candidate_version="$(jq -er '.Version' "$project_root/manifest.json")"
generated_utc="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"

{
  echo "# v0.5.0 multiplayer acceptance evidence"
  echo
  echo "Generated: \`$generated_utc\`"
  echo
  echo "## Candidate identity"
  echo
  echo "- Source commit: \`$source_commit\`"
  echo "- Manifest version: \`$candidate_version\`"
  echo "- Shared EvilFarmOwner.dll SHA-256: \`$host_sha256\`"
  echo "- Host DLL: \`$(basename "$host_dll")\`"
  echo "- Farmhand DLL: \`$(basename "$farmhand_dll")\`"
  echo "- Host log: \`$(basename "$host_log")\`"
  echo "- Farmhand log: \`$(basename "$farmhand_log")\`"
  echo
  echo "## Automated checks"
  echo
  echo "- [x] Both peer DLL hashes are identical."
  echo "- [x] Both logs contain at least two \`efo_netstatus\` records."
  echo "- [x] Final roles are host and farmhand."
  echo "- [x] Final host session agrees: \`$host_session\`."
  echo "- [x] Final active contract agrees: \`$host_active\`."
  echo "- [x] Host recovery and quarantine health are true."
  echo "- [x] No Evil Farm Owner error or unhandled-exception line was found."
  echo
  echo "## Final network status"
  echo
  echo "- Host: \`$host_status\`"
  echo "- Farmhand: \`$farmhand_status\`"
  echo
  echo "## Required run metadata"
  echo
  echo "- Stardew Valley version: TODO"
  echo "- SMAPI version: TODO"
  echo "- Save ID and in-game date: TODO"
  echo "- Host and farmhand player IDs: TODO"
  echo "- Starting/ending money and item counts: TODO"
  echo "- Roster/contract screenshot: TODO"
  echo
  echo "## One-pass matrix"
  echo
  echo "| Row | Result | Evidence note |"
  echo "| --- | --- | --- |"
  echo "| 1. One-worker baseline | TODO | |"
  echo "| 2. Four-worker deterministic work | TODO | |"
  echo "| 3. Standby and failed-stage reassignment | TODO | |"
  echo "| 4. Route and storage contention | TODO | |"
  echo "| 5. Departure, disconnect, and reconnect | TODO | |"
  echo "| 6. Save boundary, day end, and host restart | TODO | |"
  echo "| 7. Rejection and configuration ownership | TODO | |"
  echo
  echo "The automated checks above do not replace the manual matrix. Replace every TODO and attach both complete logs before marking PR #143 ready."
} > "$output_path"

echo "Wrote multiplayer acceptance evidence summary: $output_path"
